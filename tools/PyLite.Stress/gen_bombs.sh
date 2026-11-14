#!/bin/bash
# One probe file per operation, so the harness's steps column is that operation's own count.
# All sources are backslash-free on purpose (the shell tooling halves backslashes).
# Regenerates bombs/ next to this script; run from tools/PyLite.Stress with Git Bash.
D="$(dirname "$0")/bombs"
rm -rf "$D"; mkdir -p "$D"
mk() { printf '# deadline: 20000\n# allocmb: 1024\n%s\n' "$2" > "$D/$1.py"; }
N=8000000

# ---- json ----
mk j1_invalid_tail "import json
s = '[' + '1,' * 1500000 + '1] x'
try:
    json.loads(s)
except ValueError as e:
    print(type(e).__name__)"
mk j2_valid_big "import json
s = '[' + '1,' * 1500000 + '1]'
print(len(json.loads(s)))"
mk j3_many_spaces "import json
s = '[' + ('          1,' * 400000) + '1]'
print(len(json.loads(s)))"
mk j4_many_strings "import json
s = '[' + ('\"abcdefgh\",' * 400000) + '\"z\"]'
print(len(json.loads(s)))"
mk j5_dumps_big "import json
print(len(json.dumps([1] * 1500000)))"
mk j6_deep_nested "import json
try:
    json.loads('[' * 100000 + ']' * 100000)
except ValueError as e:
    print('clean', type(e).__name__)"
mk j7_parse_float_hook "import json
s = '[' + '1.5,' * 300000 + '1.5]'
print(len(json.loads(s, parse_float=lambda x: x)))"

# ---- codecs ----
mk c1_encode_cp866 "s = chr(1046) * $N
print(len(s.encode('cp866')))"
mk c2_decode_cp866 "b = bytes([134]) * $N
print(len(b.decode('cp866')))"
mk c3_encode_utf8 "s = chr(1046) * $N
print(len(s.encode('utf-8')))"
mk c4_decode_utf16 "b = bytes([54, 4]) * 4000000
print(len(b.decode('utf-16-le')))"
mk c5_encode_replace "s = chr(8364) * $N
print(len(s.encode('cp866', 'replace')))"

# ---- urllib ----
mk u1_quote "import urllib.parse as up
print(len(up.quote('a b/' * 2000000)))"
mk u2_unquote "import urllib.parse as up
print(len(up.unquote('%20a' * 2000000)))"
mk u3_parse_qs "import urllib.parse as up
print(len(up.parse_qs('a=1&' * 1000000)))"
mk u4_urlencode "import urllib.parse as up
print(len(up.urlencode({str(i): 'x y' for i in range(300000)})))"
mk u5_urlsplit_long "import urllib.parse as up
print(len(up.urlsplit('http://h/' + 'p/' * 2000000).path))"
mk u6_urljoin_taught "import urllib.parse as up
for i in range(200000):
    up.uses_relative.append('s' + str(i))
print(up.urljoin('zzz://h/a', 'b'))"

# ---- bytes text methods, str case ----
mk b1_bytes_upper "b = b'abc ' * 2000000
print(len(b.upper()))"
mk b2_bytes_split "b = b'abc ' * 2000000
print(len(b.split()))"
mk b3_bytes_replace "b = b'abc ' * 2000000
print(len(b.replace(b'a', b'xy')))"
mk b4_bytes_title "b = b'abc ' * 2000000
print(len(b.title()))"
mk b5_bytes_isalpha "b = b'abcd' * 2000000
print(b.isalpha())"
mk s1_str_upper "s = chr(1078) * $N
print(len(s.upper()))"
mk s2_str_casefold "s = chr(223) * $N
print(len(s.casefold()))"

# ---- re ----
mk r1_compile_long "import re
p = re.compile('a' * 1000000)
print(p.match('a' * 1000000) is not None)"
mk r2_compile_named "import re
p = re.compile('[' + chr(176) + ']' * 100000)
print(p is not None)"
mk r3_counted_repeats "import re
p = re.compile('(?:a{,3}b){1,4}' * 50000)
print(p is not None)"

# ---- hashlib / gzip / zlib / struct ----
mk h1_sha224 "import hashlib
print(hashlib.sha224(b'x' * $N).hexdigest()[:8])"
mk h2_pbkdf2_iters "import hashlib
print(hashlib.pbkdf2_hmac('sha256', b'p', b's', 3000000).hex()[:8])"
mk g1_gzip_bomb "import gzip
z = gzip.compress(bytes(50000000))
print(len(z))
try:
    print(len(gzip.decompress(z)))
except MemoryError as e:
    print('clean MemoryError')"
sed -i 's/^# allocmb: 1024$/# allocmb: 64/' "$D/g1_gzip_bomb.py"
mk g2_zlib_bomb "import zlib
z = zlib.compress(bytes(50000000))
try:
    print(len(zlib.decompress(z)))
except MemoryError as e:
    print('clean MemoryError')"
sed -i 's/^# allocmb: 1024$/# allocmb: 64/' "$D/g2_zlib_bomb.py"
mk t1_struct_iter_unpack "import struct
print(sum(1 for _ in struct.iter_unpack('i', bytes(4 * 2000000))))"

# ---- csv ----
mk v1_reader_many_fields "import csv
print(len(next(csv.reader(['a,' * 2000000 + 'a']))))"
mk v2_reader_huge_field "import csv
csv.field_size_limit(2000000000)
print(len(next(csv.reader(['\"' + 'x' * $N + '\"']))[0]))"
mk v3_writer_many_fields "import csv
from io import StringIO
b = StringIO()
csv.writer(b).writerow(['x'] * 2000000)
print(len(b.getvalue()))"
mk v4_sniff_big "import csv
print(csv.Sniffer().sniff('a;b;c' + chr(10)) is not None)
print(csv.Sniffer().sniff(('a;b;c' + chr(10)) * 400000).delimiter)"
mk v5_has_header_big "import csv
print(csv.Sniffer().has_header(('a;b;c' + chr(10)) * 400000))"
mk v6_dictreader_rows "import csv
rows = ['a,b'] + ['1,2'] * 500000
print(sum(1 for _ in csv.DictReader(rows)))"
mk v7_duck_dialect_loop "import csv
class D:
    def __init__(self):
        self.delimiter = ';'
n = 0
for i in range(200000):
    n += len(next(csv.reader(['a;b'], D())))
print(n)"

# ---- xml ----
mk x1_fromstring_flat "import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a>t</a>' * 500000 + '</r>')
print(len(r))"
mk x2_tostring_html_wide "import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(500000):
    ET.SubElement(r, 'br')
print(len(ET.tostring(r, encoding='unicode', method='html')))"
mk x3_tostring_text_wide "import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(500000):
    ET.SubElement(r, 'a').text = 't'
print(len(ET.tostring(r, encoding='unicode', method='text')))"
mk x4_deep_built_tostring "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
print(len(ET.tostring(r, encoding='unicode')))"
mk x5_deep_built_iter "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
print(sum(1 for _ in r.iter()))"
mk x6_deep_built_itertext "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd'); e.text = 't'
print(len(''.join(r.itertext())))"
mk x7_deep_built_parentmap "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
print(len(r.findall('.//d/..')))"
mk x8_deep_built_indent "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
ET.indent(r)
print(len(ET.tostring(r, encoding='unicode')))"
mk x9_deep_built_xmlid "import xml.etree.ElementTree as ET
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd'); e.set('id', str(i))
print(len(ET.tostring(r, encoding='unicode')))"
mk xa_deep_built_deepcopy "import xml.etree.ElementTree as ET, copy
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
print(len(copy.deepcopy(r)))"
mk xb_xpath_text_quadratic "import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a>t<b>u</b></a>' * 30000 + '</r>')
print(len(r.findall('.//*[.=\"zzz\"]')))"
mk xc_xmlid_big "import xml.etree.ElementTree as ET
s = '<r>' + ''.join('<a id=\"i%d\"/>' % i for i in range(300000)) + '</r>'
print(len(ET.XMLID(s)[1]))"
mk xd_iterparse_big "import xml.etree.ElementTree as ET
from io import StringIO
s = '<r>' + '<a>t</a>' * 500000 + '</r>'
print(sum(1 for _ in ET.iterparse(StringIO(s))))"
mk xe_find_wide "import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a/>' * 500000 + '<z/></r>')
print(r.find('z').tag)"

# ---- deep data: repr / eq / hash / deepcopy / dumps ----
mk d1_deep_list_repr "l = []
for _ in range(100000):
    l = [l]
try:
    print(len(repr(l)))
except RecursionError as e:
    print('clean RecursionError')"
mk d2_deep_list_eq "l = []; m = []
for _ in range(100000):
    l = [l]; m = [m]
try:
    print(l == m)
except RecursionError as e:
    print('clean RecursionError')"
mk d3_deep_tuple_hash "t = ()
for _ in range(100000):
    t = (t,)
try:
    print(hash(t) is not None)
except RecursionError as e:
    print('clean RecursionError')"
mk d4_deep_deepcopy "import copy
l = []
for _ in range(100000):
    l = [l]
try:
    print(len(copy.deepcopy(l)))
except RecursionError as e:
    print('clean RecursionError')"
mk d5_deep_json_dumps "import json
l = []
for _ in range(100000):
    l = [l]
try:
    print(len(json.dumps(l)))
except (RecursionError, ValueError) as e:
    print('clean', type(e).__name__)"
mk d6_deep_dataclass_eq "from dataclasses import dataclass
@dataclass
class N:
    child: object
a = None; b = None
for _ in range(100000):
    a = N(a); b = N(b)
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')"
mk d7_deep_dataclass_hash "from dataclasses import dataclass
@dataclass(frozen=True)
class N:
    child: object
a = None
for _ in range(100000):
    a = N(a)
try:
    print(hash(a) is not None)
except RecursionError as e:
    print('clean RecursionError')"
mk d8_deep_record_repr "class N:
    def __init__(self, c):
        self.c = c
a = None
for _ in range(100000):
    a = N(a)
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')"


# ---- opaque containers whose ==/hash/repr re-enter the engine with their children ----
mk d9_deep_deque_eq "from collections import deque
a = deque(); b = deque()
for _ in range(100000):
    a = deque([a]); b = deque([b])
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')"

mk da_deep_deque_repr "from collections import deque
a = deque()
for _ in range(100000):
    a = deque([a])
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')"

mk db_deep_slice_repr "a = slice(0)
for _ in range(100000):
    a = slice(a)
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')"

mk dc_deep_partial_repr "from functools import partial
def f(*a): pass
p = partial(f)
for _ in range(100000):
    p = partial(f, p)
try:
    print(len(repr(p)))
except RecursionError as e:
    print('clean RecursionError')"

mk dd_deep_chainmap_eq "from collections import ChainMap
a = ChainMap({}); b = ChainMap({})
for _ in range(100000):
    a = ChainMap({'k': a}); b = ChainMap({'k': b})
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')"

mk de_deep_exception_repr "e = ValueError(1)
for _ in range(100000):
    e = ValueError(e)
try:
    print(len(repr(e)))
except RecursionError as e:
    print('clean RecursionError')"

# ---- record protocol hooks ----
mk p1_eq_recurses "class A:
    def __eq__(self, o):
        return self == o
try:
    print(A() == A())
except RecursionError as e:
    print('clean RecursionError')"
mk p2_hash_recurses "class A:
    def __hash__(self):
        return hash(self)
try:
    print({A()})
except RecursionError as e:
    print('clean RecursionError')"
mk p3_str_recurses "class A:
    def __str__(self):
        return str(self)
try:
    print(str(A()))
except RecursionError as e:
    print('clean RecursionError')"
mk p4_len_recurses "class A:
    def __len__(self):
        return len(self)
try:
    print(len(A()))
except RecursionError as e:
    print('clean RecursionError')"
mk p5_bool_recurses "class A:
    def __bool__(self):
        return bool(self)
try:
    print(bool(A()))
except RecursionError as e:
    print('clean RecursionError')"
mk p6_iter_never_ends "class A:
    def __iter__(self):
        return self
    def __next__(self):
        return 1
print(len(list(A())))"
mk p7_getitem_never_ends "class A:
    def __getitem__(self, i):
        return i
print(len(list(A())))"
mk p8_contains_recurses "class A:
    def __contains__(self, x):
        return x in self
try:
    print(1 in A())
except RecursionError as e:
    print('clean RecursionError')"
mk p9_id_many "class A:
    pass
print(len({id(A()) for _ in range(1000000)}))"
mk pa_call_recurses "class A:
    def __call__(self):
        return self()
try:
    print(A()())
except RecursionError as e:
    print('clean RecursionError')"

# ---- statistics / decimal ----
mk m1_stdev_decimal "import statistics
from decimal import Decimal
print(statistics.stdev([Decimal(i) for i in range(300000)]))"
mk m2_median_decimal "import statistics
from decimal import Decimal
print(statistics.median([Decimal(i) for i in range(300000)]))"
mk m3_decimal_ln_loop "from decimal import Decimal
s = Decimal(0)
for i in range(1, 200000):
    s += Decimal(i).ln()
print(s > 0)"
mk m4_decimal_extremes "from decimal import Decimal, getcontext
r = []
for e in ('1e27', '1e-27', '9.9999999999999999999999999e27', '1e-28'):
    try:
        r.append(str(Decimal(e).sqrt()))
    except Exception as ex:
        r.append(type(ex).__name__)
try:
    r.append(str(Decimal('1e27').exp()))
except Exception as ex:
    r.append(type(ex).__name__)
print(r)"

# ---- misc touched today ----
mk w1_html_unescape "import html
print(len(html.unescape('&amp;&lt;&#1046;' * 500000)))"
mk w2_strftime_long_fmt "import datetime
print(len(datetime.datetime(2020, 1, 1).strftime('%Y-' * 1000000)))"
mk w3_strptime_long "import datetime
try:
    datetime.datetime.strptime('x' * 2000000, '%Y')
except ValueError as e:
    print('clean ValueError')"
mk w4_fromisoformat_long "import datetime
try:
    datetime.datetime.fromisoformat('2020-01-01T' + '1' * 2000000)
except ValueError as e:
    print('clean ValueError')"
mk w5_percent_format_many "print(len(('%s,' * 500000) % tuple(range(500000))))"
mk w6_textwrap_big "import textwrap
print(len(textwrap.wrap('word ' * 1000000, 80)))"
mk w7_bisect_insort "import bisect
l = []
for i in range(200000):
    bisect.insort(l, -i)
print(len(l))"
mk w8_heapq_big "import heapq
print(heapq.nlargest(5, range(3000000)))"
mk w9_deque_rotate "from collections import deque
d = deque(range(1000000))
d.rotate(500000)
print(d[0])"
mk wa_calendar_year "import calendar
print(len(calendar.calendar(2024)))"
mk wb_unicodedata_lookup_loop "import unicodedata
n = 0
for i in range(300000):
    n += len(unicodedata.name(chr(0x400 + i % 200), ''))
print(n)"
mk wc_operator_itemgetter "import operator
f = operator.itemgetter(*range(200000))
print(len(f(list(range(200000)))))"
mk wd_counter_most_common "from collections import Counter
print(Counter('ab' * 2000000).most_common(1))"
mk we_bytes_fromhex "print(len(bytes.fromhex('ab' * 4000000)))"
mk wf_int_str_huge "print(len(str(7 ** 400000)))"

# ---- zipfile ----
mk z1_zip_deflate_bomb "import io
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('zeros', bytes(15000000))
zf = zipfile.ZipFile(io.BytesIO(buf.getvalue()))
try:
    for i in range(20):
        zf.read('zeros')
    print('read all')
except MemoryError as e:
    print('clean MemoryError')"
sed -i 's/^# allocmb: 1024$/# allocmb: 64/' "$D/z1_zip_deflate_bomb.py"
mk z2_zip_nested "import io
import zipfile
def pack(members):
    b = io.BytesIO()
    with zipfile.ZipFile(b, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in members:
            z.writestr(name, data)
    return b.getvalue()
inner = pack([('zeros', bytes(15000000))])
outer = pack([('i%d.zip' % k, inner) for k in range(4)])
top = pack([('o%d.zip' % k, outer) for k in range(4)])
def walk(data):
    total = 0
    with zipfile.ZipFile(io.BytesIO(data)) as z:
        for n in z.namelist():
            m = z.read(n)
            total += walk(m) if n.endswith('.zip') else len(m)
    return total
print(len(top), walk(top))"
sed -i 's/^# allocmb: 1024$/# allocmb: 64/' "$D/z2_zip_nested.py"
mk z3_zip_many_entries "import io
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w') as z:
    for i in range(300000):
        z.writestr('f%d' % i, b'')
print(len(zipfile.ZipFile(io.BytesIO(buf.getvalue())).namelist()))"
mk z4_zip_overlap "import io
import struct
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w') as z:
    z.writestr('a', b'first member data')
    z.writestr('b', b'second member data')
blob = buf.getvalue()
cd = blob.find(b'PK' + bytes([1, 2]))
blob = blob[:cd + 20] + struct.pack('<LL', 100000, 100000) + blob[cd + 28:]
zf = zipfile.ZipFile(io.BytesIO(blob))
print(zf.read('b'))
try:
    zf.read('a')
except zipfile.BadZipFile as e:
    print('clean BadZipFile')"
mk z5_zip_forged_size "import io
import struct
import zipfile
buf = io.BytesIO()
with zipfile.ZipFile(buf, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('zeros', bytes(15000000))
blob = buf.getvalue()
blob = blob[:22] + struct.pack('<L', 2147483647) + blob[26:]
cd = blob.find(b'PK' + bytes([1, 2]))
blob = blob[:cd + 24] + struct.pack('<L', 2147483647) + blob[cd + 28:]
print(len(zipfile.ZipFile(io.BytesIO(blob)).read('zeros')))"
sed -i 's/^# allocmb: 1024$/# allocmb: 64/' "$D/z5_zip_forged_size.py"
mk z6_zip_not_an_archive "import io
import zipfile
print(zipfile.is_zipfile(io.BytesIO(b'x' * 8000000)))
try:
    zipfile.ZipFile(io.BytesIO(b'PK' * 4000000))
except zipfile.BadZipFile as e:
    print('clean BadZipFile')"
# ---- l: the limits audit of 2026-10-29 (each probe found or pins a fixed defect) ----
# hdr <probe> <tag> <value>: set or add one header of an already written probe.
hdr() { if grep -q "^# $2:" "$D/$1.py"; then sed -i "s/^# $2: .*/# $2: $3/" "$D/$1.py"; else sed -i "1i # $2: $3" "$D/$1.py"; fi; }
# Leaf re-entry under a host-raised MaxDataDepth (clamped to the 10000 hard cap): it used to overflow the stack.
mk l1_reentry_deque_repr "from collections import deque
d = deque()
for i in range(200000):
    d = deque([d])
try:
    repr(d)
except RecursionError:
    print('clean RecursionError')"
mk l2_reentry_exc_repr "e = ValueError()
for i in range(200000):
    e = ValueError(e)
try:
    repr(e)
except RecursionError:
    print('clean RecursionError')"
mk l3_reentry_slice_str "s = slice(1)
for i in range(200000):
    s = slice(s)
try:
    str(s)
except RecursionError:
    print('clean RecursionError')"
mk l4_reentry_asdict "from dataclasses import dataclass, asdict
@dataclass
class N:
    n: object = None
a = N()
for i in range(200000):
    a = N(a)
try:
    asdict(a)
except RecursionError:
    print('clean RecursionError')"
for p in l1_reentry_deque_repr l2_reentry_exc_repr l3_reentry_slice_str l4_reentry_asdict; do hdr $p datadepth 1000000; done
# functools.partial: flattening a chain copied the bound args uncharged; a ** merge was quadratic.
mk l5_partial_chain "import functools
p = functools.partial(print)
for i in range(100000):
    p = functools.partial(p, 1)
print(len(p.args))"
mk l6_partial_big_args_calls "import functools
p = functools.partial(max, *range(1000000))
n = 0
while True:
    n = p()"
hdr l6_partial_big_args_calls deadline 3000
mk l7_partial_kw_merge "import functools
d = {('k' + str(i)): i for i in range(200000)}
e = {('k' + str(i)): -i for i in range(200000)}
q = functools.partial(functools.partial(dict, **d), **e)
print(len(q()))"
# time.sleep past SleepTotalMs fails at once (the default policy used to cut it to 5 s silently).
mk l8_sleep_past_total "import time
time.sleep(3)"
hdr l8_sleep_past_total sleepms 1000
# Long digit strings: a decimal one past 4,300 digits is CPython's ValueError at once; in a power-of-two base
# there is no digit limit, so the parse is refused by count or checks the deadline.
mk l9_int_overlong "x = int('9' * 1000000)"
mk la_int_base32_long "x = int('v' * 190000, 32)"
hdr la_int_base32_long deadline 3000
mk lb_bit_length_1m "x = 1 << 999999
print(x.bit_length(), (x - 1).bit_count())"
mk lc_decimal_overlong "from decimal import Decimal
try:
    Decimal('9' * 1000000)
except ArithmeticError as e:
    print('clean ' + type(e).__name__)"
mk ld_decimal_long_fraction "from decimal import Decimal
print(Decimal('0.' + '9' * 1000000))"
mk le_fraction_overlong "from fractions import Fraction
x = Fraction('9' * 1000000)"
mk lf_fraction_power_of_ten "from fractions import Fraction
x = Fraction('1e999999999')"
mk lg_fraction_pow "from fractions import Fraction
x = Fraction(3, 7) ** 1000000"
mk lh_fraction_big_parts "from fractions import Fraction
a = Fraction(3 ** 300000, 7 ** 170000)
b = Fraction(5 ** 200000, 11 ** 140000)
print(a + b > 0)"
hdr lh_fraction_big_parts deadline 3000
# One long big-int call ends at the deadline (BigOps), not after its own length.
mk li_modpow_huge "x = pow(3, (1 << 100000) - 1, (1 << 100000) + 1)"
mk lj_modinverse_huge "x = pow(7 ** 200000, -1, (1 << 999990) + 1)"
mk lk_isqrt_1m "import math
print(math.isqrt(1 << 999990) == 1 << 499995)"
mk ll_gcd_huge "import math
import random
random.seed(1)
a = random.getrandbits(999000)
b = random.getrandbits(998000)
x = math.gcd(a, b)"
mk lm_str_huge "x = str(3 ** 600000)"
for p in li_modpow_huge lj_modinverse_huge ll_gcd_huge lm_str_huge; do hdr $p deadline 3000; done
hdr lm_str_huge steps 2000000000
# int.from_bytes / to_bytes were a shift per byte; to_bytes allocated before the bytes cap.
mk ln_from_bytes_huge "x = int.from_bytes(bytes([255]) * 10000000, 'big')"
mk lo_to_bytes_big "x = 7 ** 280000
print(len(x.to_bytes((x.bit_length() + 7) // 8, 'big')))"
mk lp_to_bytes_len_cap "x = (5).to_bytes(300000000, 'big')"
# Exact at the MaxIntBits edge, and no power built past twice the value.
mk lq_round_fraction_far "from fractions import Fraction
print(round(Fraction(1, 3), -400000))"
mk lr_pow_exact_cap "print((2 ** 999999).bit_length())"

# ---- native calls that outlived their run (2026-10-30): one .NET call that ran past deadline + 5.25 s ----
mk n1_native_float_format "x = format(1.0, '.1000000f')"
mk n2_native_decimal_format "from decimal import Decimal
x = format(Decimal(1), '.2000000000f')"
mk n3_native_decimal_zero_exp "from decimal import Decimal
x = round(Decimal('0E-999999999'))"
mk n4_native_csv_sniff "import csv
x = csv.Sniffer().sniff(',\"a' * 21000)"
mk n5_native_intenum_str "import enum
class E(enum.IntEnum):
    A = '1' * 1000000"
mk n6_native_capwords_sep "import string
x = string.capwords('a' * 16000000, 'a' * 1000 + 'b')"
mk n7_native_pbkdf2_salt "import hashlib
x = hashlib.pbkdf2_hmac('sha256', b'k', b's' * 16000000, 1, dklen=3200)"
mk n8_native_fraction_str "from fractions import Fraction
x = str(Fraction(2 ** 999999, 3))"
mk n9_native_textwrap_split "import textwrap
x = textwrap.wrap(' a' * 8000000, break_on_hyphens=False)"
mk na_native_seed_str "import random
random.seed('x' * 16000000)"
mk nb_native_qsl_sep "from urllib.parse import parse_qsl
x = parse_qsl('a' * 16000000, separator='a' * 1000 + 'b')"
mk nc_native_regex_repeat "import re
x = re.compile('a{100000}')"
mk nd_native_range_step "r = range(0, 2 ** 999000, 3 ** 300000)
x = r[-1]   # the length, a big division (len() of it is an OverflowError, as in CPython)"
mk ne_native_variance "import statistics
x = statistics.variance([2 ** 999999, 0])"
for p in "$D"/n*_native_*.py; do hdr "$(basename "$p" .py)" deadline 3000; done

# ---- sandbox review (2026-10-30): each of these killed the process or broke it for later runs ----
mk q1_review_star_nesting "x = $(printf '[*%.0s' $(seq 3000))a$(printf ']%.0s' $(seq 3000))"
mk q2_review_exc_str_nested "e = ValueError(1)
for i in range(200000):
    e = ValueError(e)
try:
    x = str(e)
except RecursionError:
    print('clean RecursionError')"
mk q3_review_repeat_repr_nested "import itertools
r = 0
for i in range(200000):
    r = itertools.repeat(r)
try:
    x = repr(r)
except RecursionError:
    print('clean RecursionError')"
mk q4_review_counter_nested_le "from collections import Counter
a = Counter()
b = Counter()
for i in range(100000):
    x = Counter()
    x['k'] = a
    a = x
    y = Counter()
    y['k'] = b
    b = y
try:
    x = a <= b
except RecursionError:
    print('clean RecursionError')"
mk q5_review_cause_cycle_notes "e = ValueError()
for i in range(8):
    e.add_note('n' * 1000000)
raise e from e"
for p in "$D"/q*_review_*.py; do hdr "$(basename "$p" .py)" deadline 3000; done

# ---- sandbox review (2026-10-30): a loop of calls that each did long work for one step leaked the thread ----
mk k1_leak_float_parse "s = '1' * 16000000
while True:
    float(s)"
mk k2_leak_decimal_invalid "from decimal import Decimal
s = 'x' * 16000000
while True:
    try:
        Decimal(s)
    except Exception:
        pass"
mk k3_leak_str_in "s = 'a' * 8000000
while 'ab' not in s:
    pass"
mk k4_leak_sort_long_str "a = 'a' * 16000000
b = 'a' * 15999999 + 'b'
l = [a, b] * 1000
l.sort()"
mk k5_leak_set_subset "a = set(range(1000000))
b = a.copy()
while a <= b:
    pass"
mk k6_leak_chainmap_in "from collections import ChainMap
cm = ChainMap(*([{}] * 4000000))
while 'k' not in cm:
    pass"
mk k7_leak_isalnum_astral "s = chr(0x10107) * 8000000
while True:
    s.isalnum()"
mk k8_leak_stringio_readline "import io
f = io.StringIO()
for i in range(2000):
    f.write('a' * 8000)
while True:
    f.seek(0)
    f.readline()"
mk k9_leak_tee_clones "import itertools
ts = itertools.tee(range(5000), 1500000)
x = list(ts[0])"
mk ka_leak_kw_binding "def f($(seq -s ', ' -f 'a%g' 0 9999), **kw):
    pass
f(**{str(i): 0 for i in range(300000)})"
mk kb_leak_xml_cycle_iter "import xml.etree.ElementTree as ET
a = ET.Element('a')
a.append(a)
for x in a.iter('zzz'):
    pass"
mk kc_leak_hmac_long_key "import hmac
h = hmac.new(b'k' * 16000000, digestmod='sha256')
while True:
    h.update(b'')
    h.digest()"
mk kd_leak_uuid_parse "import uuid
h = 'urn:' * 4000000
while True:
    try:
        uuid.UUID(h)
    except ValueError:
        pass"
mk ke_leak_csv_terminator "import csv, io
csv.writer(io.StringIO(), lineterminator='x' * 200000).writerow(['y' * 200000])"
mk kf_leak_namedtuple_fields "from collections import namedtuple
P = namedtuple('P', 'a ' * 3000000, rename=True)"
mk kg_leak_bigint_mod "x = (1 << 999000) - 12345
while True:
    x % 7"
mk kh_leak_notes_format "import traceback
e = ValueError('x')
for i in range(100000):
    e.add_note('n')
x = traceback.format_exception_only(e)"
for p in "$D"/k*_leak_*.py; do hdr "$(basename "$p" .py)" deadline 3000; done

# ---- sandbox review (2026-10-30): memory taken far past what the budget charged ----
mk o1_mem_ascii_expand "x = ascii(chr(0x100) * 15900000)"
mk o2_mem_format_g "x = format(1.5, '.250000000g')"
mk o3_mem_print_sep "s = 'x' * 16000000
print('', 'a', 'b', 'c', sep=s)"
mk o4_mem_json_escape "import json
x = json.dumps(chr(0xe9) * 16000000)"
mk o5_mem_strftime_zone "from datetime import datetime, timezone, timedelta
tz = timezone(timedelta(0), 'x' * 1000000)
x = datetime(2020, 1, 1, tzinfo=tz).strftime('%Z' * 300)"
mk o6_mem_format_fields "s = 'a' * 1000000
x = ('{0:s}' * 400).format(s)"
mk o7_mem_fstring_cap "s = 'a' * 16000000
x = f'{s}{s}'"
mk o8_mem_star_args "import itertools
def f(*a):
    pass
f(*itertools.repeat(None))"
mk o9_mem_dist_iterator "import math, itertools
x = math.dist(itertools.repeat(1.0), [1.0])"
mk oa_mem_re_endpos_kept "import re
p = re.compile('a')
s = 'a' * 1000000
kept = [p.search(s, 0, len(s) - 1 - i) for i in range(400)]"
mk ob_mem_re_groups_kept "import re
p = re.compile('()' * 2000)
kept = list(p.finditer('a' * 4000))"
mk oc_mem_json_big_list "import json
x = len(json.dumps([0] * 5000000))"
mk od_mem_repr_big_list "x = len(repr([0] * 5000000))"
mk oe_mem_xml_extend "import xml.etree.ElementTree as ET
r = ET.Element('r')
L = [ET.Element('a')] * 4000000
for i in range(10):
    r.extend(L)"
mk of_mem_attrgetter "import operator
p = '.' * 15000000
g = operator.attrgetter(p, p, p, p)"
mk og_mem_urljoin "from urllib.parse import urljoin
x = urljoin('http://h/a/b', '../' * 5000000)"
mk oh_mem_csv_field "import csv
x = next(csv.reader(['\"'] + ['x' * 1000000] * 400))"
for p in "$D"/o*_mem_*.py; do hdr "$(basename "$p" .py)" deadline 3000; hdr "$(basename "$p" .py)" allocmb 256; done

# ---- sandbox review (2026-10-30): internal errors and item caps (P3/P4) ----
mk s1_err_xml_text_int "import xml.etree.ElementTree as ET
e = ET.Element('r')
e.text = 5
try:
    ET.tostring(e)
except TypeError as x:
    print('clean', type(x).__name__)"
mk s2_err_xml_attrkey_int "import xml.etree.ElementTree as ET
e = ET.Element('r')
e.attrib = {'{u}a': '1', 5: 'x'}
try:
    ET.tostring(e)
except TypeError as x:
    print('clean', type(x).__name__)"
mk s3_err_xml_child_int "import xml.etree.ElementTree as ET
e = ET.Element('r')
ET.SubElement(e, 'a')
try:
    e[0] = 5
except TypeError as x:
    print('clean', type(x).__name__)"
mk s4_err_xml_tail_textpred "import xml.etree.ElementTree as ET
e = ET.Element('r')
c = ET.SubElement(e, 'c')
c.tail = 5
try:
    e.findall(\".[.='z']\")
except TypeError as x:
    print('clean', type(x).__name__)"
mk s5_err_decimal_noarg "import decimal
try:
    decimal.Decimal(1).compare()
except TypeError as x:
    print('clean', type(x).__name__)"
mk s6_err_quantiles_overflow "import statistics
print(round(statistics.quantiles(range(100001), n=30000, method='inclusive')[-1], 3))"
mk s7_err_re_verbose_twin "import re
print(bool(re.fullmatch('(?x)a # trailing comment', 'a')))"
mk s8_err_re_pos_after_endpos "import re
p = re.compile('a')
print(p.search('aaa', 2, 1), p.findall('aaa', 2, 1), list(p.finditer('aaa', 2, 1)))"
mk s9_err_normalize_nonchar "import unicodedata
s = chr(0xFFFF) * 4000000
print(unicodedata.normalize('NFC', s) == s)"
mk sa_err_zip_header_offset "import io, zipfile
b = io.BytesIO()
with zipfile.ZipFile(b, 'w') as z:
    z.writestr('a', b'hello')
zf = zipfile.ZipFile(io.BytesIO(b.getvalue()))
zi = zf.getinfo('a')
zi.header_offset = 2**63 - 16
try:
    zf.read(zi)
except zipfile.BadZipFile as x:
    print('clean', type(x).__name__)"
mk sb_cap_struct_zero_s "import struct
x = struct.unpack('0s' * 6000000, b'')"
mk sc_cap_cwr_tuple "import itertools
x = next(itertools.combinations_with_replacement([1], 20000000))"
mk sd_cap_product_repeat "import itertools
x = next(itertools.product([1], repeat=12000000))"
mk se_cap_list_slice_assign "x = [0] * 4000000
x[0:0] = [0] * 4000000"
mk sf_cap_list_insert "x = [0] * 5000000
x.insert(0, 1)"
for p in "$D"/s*_err_*.py "$D"/s*_cap_*.py; do hdr "$(basename "$p" .py)" deadline 3000; hdr "$(basename "$p" .py)" allocmb 256; done

# ---- sandbox review 2 (2026-10-30): a crash, a run that broke later runs, memory with no charge (P0) ----
mk e1_sof_defaultdict_self_factory "import collections
d = collections.defaultdict(list)
d.default_factory = d
print(repr(d))"
mk e2_sof_defaultdict_factory_chain "import collections
a = collections.defaultdict(None)
for i in range(500000):
    b = collections.defaultdict(None)
    b.default_factory = a
    a = b
print(len(repr(a)))"
mk e3_sof_except_nested_tuple "t = KeyError
for i in range(700000):
    t = (t,)
try:
    try:
        raise KeyError('k')
    except t:
        pass
except TypeError as x:
    print('clean', type(x).__name__)"
mk e4_mem_itemgetter_chain "import operator
g = None
for _ in range(5000000):
    g = operator.itemgetter(g)"
mk e5_mem_lru_wrapper_chain "import functools
d = functools.lru_cache(maxsize=0)
f = len
for _ in range(15000000):
    f = d(f)"
mk e6_mem_cmp_to_key_chain "import functools
K = functools.cmp_to_key(lambda a, b: 0)
k = None
for _ in range(15000000):
    k = K(k)"
mk e7_mem_format_precision_kept "s = 'a' * 8000000
L = []
for i in range(40):
    L.append(format(s, '.7999999'))"
mk e8_mem_finditer_endpos_kept "import re
p = re.compile('a')
s = 'b' * 4000000
its = []
for i in range(60):
    it = p.finditer(s, 0, len(s) - 1 - i)
    next(it, None)
    its.append(it)"
mk e9_mem_dumps_separator_per_depth "import json
x = 1
for i in range(100):
    x = [x]
s = json.dumps(x, separators=('y' * 2000000, ':'), indent=0)"
mk ea_mem_dumps_indent_per_depth "import json
n = [0]
def f(o):
    n[0] += 1
    if n[0] < 120:
        return iter([])
    return [1]
s = json.dumps(iter([]), indent='x' * 5000, default=f)"
mk eb_mem_class_fields "from dataclasses import make_dataclass
L = []
for i in range(150):
    L.append(make_dataclass('C', ['a%d' % j for j in range(35000)]))"
mk ec_cap_namedtuple_defaults_repeat "from collections import namedtuple
import itertools
P = namedtuple('P', 'a', defaults=itertools.repeat(0, 20000000))"
mk ed_cap_dictwriter_fieldnames_repeat "import csv, io, itertools
w = csv.DictWriter(io.StringIO(), itertools.repeat('a', 20000000))"
mk ee_cap_join_empty_repeat "import itertools
x = ''.join(itertools.repeat('', 25000000))"
mk ef_cap_writerow_repeat_none "import csv, io, itertools
csv.writer(io.StringIO()).writerow(itertools.repeat(None, 20000000))"
for p in "$D"/e*_sof_*.py "$D"/e*_mem_*.py "$D"/e*_cap_*.py; do hdr "$(basename "$p" .py)" deadline 3000; hdr "$(basename "$p" .py)" allocmb 256; done

# ---- sandbox review 2 (2026-10-30): work charged one step, which leaked the runner thread (P2) ----
mk f1_leak_fused_or_filter "r = [0 for x in range(10**9) if $(printf 'x<0 or %.0s' $(seq 64999))x<0]"
mk f2_leak_compiled_sum "g = 0
r = [0 for x in range(10**9) if $(for i in $(seq 399); do printf '(%s)+' "$(printf 'g+%.0s' $(seq 499))g"; done)($(printf 'g+%.0s' $(seq 499))g)]"
mk f3_leak_if_clauses "r = [0 for x in range(10**9) $(printf 'if 1 %.0s' $(seq 99000))]"
mk f4_leak_kwonly_defaults "def f(*, $(for i in $(seq 0 44998); do printf 'a%d=0,' $i; done)a44999=0):
    pass
while True:
    f()"
mk f5_leak_class_init_walk "while True:
    class C:
        def __init__(self):
            self.a = 0$(printf ';self.a=0%.0s' $(seq 49999))"
mk f6_leak_list_del_front "L = [0] * 5000000
while L:
    del L[0]"
mk f7_leak_startswith_prefixes "s = 'a' * 8000000
t = (s[:-1] + 'b',) * 3000
x = s.startswith(t)"
mk f8_leak_bytes_tuple_eq "a = b'a' * 16000000
b = b'a' * 15999999 + b'a'
x = (a,) * 1000 == (b,) * 1000"
mk f9_leak_slice_indices_big "n = 2**999999
s = slice(None, None, 3**315000)
while True:
    s.indices(n)"
mk fa_leak_bigint_floordiv "a = 2**999999
b = 3**630700
while True:
    a // b"
mk fb_leak_asdict_dag "from dataclasses import dataclass, asdict
@dataclass
class N:
    a: object
    b: object
x = N(None, None)
for i in range(40):
    x = N(x, x)
asdict(x)"
mk fc_leak_randbytes "import random
while True:
    random.randbytes(15000000)"
mk fd_leak_unescape_legacy "import html
while True:
    html.unescape(('&' + 'q' * 31) * 100000)"
mk fe_leak_attrgetter_path "import operator
class C:
    def __init__(self):
        self.a = self
c = C()
f = operator.attrgetter('a.' * 2999999 + 'a')
while True:
    f(c)"
mk ff_leak_deque_imul_maxlen "from collections import deque
d = deque([0] * 2000000, maxlen=2000000)
while True:
    d *= 10**18"
mk fg_leak_capwords_words "import string
x = string.capwords('a ' * 8000000)"
mk fh_cap_match_star_range "match range(2**40):
    case [a, *rest]:
        pass"
for p in "$D"/f*_leak_*.py "$D"/f*_cap_*.py; do hdr "$(basename "$p" .py)" deadline 3000; hdr "$(basename "$p" .py)" allocmb 256; done
# ---- y: the second sandbox review, memory and native loops (P4) ----
# An output built in full before its size was checked, parts held uncharged, a walk that built every match
# first, a native loop per call on one step. Each ends on the limit it would cross, or on its own result.
mk y1_cap_quote_expand "import urllib.parse
x = urllib.parse.quote(chr(0) * 6000000)"
mk y2_cap_re_escape "import re
x = re.escape('.' * 9000000)"
mk y3_cap_xml_attr_escape "import xml.etree.ElementTree as ET
x = ET.tostring(ET.Element('a', b=chr(34) * 3000000))"
mk y4_cap_normalize_expand "import unicodedata
x = unicodedata.normalize('NFKD', chr(0xfdfa) * 1000000)"
mk y5_cap_bytes_hex_sep "x = (b'a' * 6000000).hex(':')"
mk y6_cap_encode_utf16 "x = ('a' * 9000000).encode('utf-16')"
mk y7_cap_replace_grow "x = ('a' * 6000000).replace('a', 'bbb')"
mk y8_cap_b32encode "import base64
x = base64.b32encode(b'a' * 11000000)"
mk y9_cap_csv_field_limit_raised "import csv
csv.field_size_limit(2147483647)
x = next(csv.reader([chr(34)] + ['x' * 1000000] * 120))"
mk ya_mem_rsplit_parts "x = ('a ' * 8000000).rsplit()"
mk yb_mem_bytes_split_parts "x = (b'a ' * 8000000).split()"
mk yc_mem_shorten_words "import textwrap
x = textwrap.shorten('a ' * 8000000, 10)"
mk yd_mem_parse_qs_parts "import urllib.parse
x = urllib.parse.parse_qs('a&' * 8000000)"
mk ye_mem_counter_elements_kept "from collections import Counter
c = Counter(range(1000000))
its = []
for i in range(12):
    its.append(c.elements())
    next(its[-1])
print(len(its))"
mk yf_mem_xml_find_dag "import xml.etree.ElementTree as ET
R = ET.Element('r')
A = ET.Element('a')
A.extend([ET.Element('b')] * 6000)
R.extend([A] * 6000)
print(R.find('*/*').tag)"
mk yg_mem_xml_iterfind_dag "import xml.etree.ElementTree as ET
R = ET.Element('r')
A = ET.Element('a')
A.extend([ET.Element('b')] * 4000)
R.extend([A] * 4000)
for e in R.iterfind('*/*'):
    pass"
mk yh_mem_xml_itertext_dag "import xml.etree.ElementTree as ET
R = ET.Element('r')
A = ET.Element('a')
B = ET.Element('b')
B.text = 'b'
A.extend([B] * 4000)
R.extend([A] * 4000)
for t in R.itertext():
    pass"
mk yi_cap_xml_subelement "import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(3000):
    ET.SubElement(r, 'a')"
mk yj_steps_re_sub_empty "import re
x = re.sub('', '', 'a' * 4000000)"
mk yk_steps_re_split_empty "import re
x = re.split('', 'a' * 2000000)"
mk yl_steps_decode_error_scan "import json
d = 'x' * 16000000
for i in range(300):
    e = json.JSONDecodeError('m', d, len(d))"
mk ym_steps_b32encode "import base64
s = b'a' * 9000000
for i in range(40):
    x = base64.b32encode(s)"
mk yn_cap_factorial "import math
x = math.factorial(10**7)"
for p in "$D"/y*.py; do hdr "$(basename "$p" .py)" deadline 3000; hdr "$(basename "$p" .py)" allocmb 64; done
hdr yi_cap_xml_subelement items 1000
for p in yj_steps_re_sub_empty yk_steps_re_split_empty; do hdr $p steps 100000; done
hdr yl_steps_decode_error_scan steps 20000
hdr ym_steps_b32encode steps 10000
hdr ym_steps_b32encode deadline 60000
hdr ym_steps_b32encode allocmb 1024
hdr ye_mem_counter_elements_kept allocmb 256
for p in ya_mem_rsplit_parts yb_mem_bytes_split_parts yc_mem_shorten_words yd_mem_parse_qs_parts; do hdr $p deadline 20000; done
ls "$D" | wc -l
