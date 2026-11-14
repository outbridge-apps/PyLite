import datetime, csv, io, decimal, json, base64, hmac, hashlib, urllib.parse, random, time, math, re, dataclasses, collections, statistics
import xml.etree.ElementTree as ET
from decimal import Decimal
from fractions import Fraction
def t(label, f):
    try:
        r = f()
        print(label, 'OK', repr(r))
    except BaseException as e:
        print(label, type(e).__name__, str(e))
d = datetime.date(2024, 12, 30)
t('iso', lambda: d.isocalendar())
t('iso_week', lambda: d.isocalendar().week)
t('iso_repr', lambda: repr(datetime.datetime(2024, 1, 1).isocalendar()))
t('strf_G', lambda: d.strftime('%G-%V-%u'))
t('strf_e', lambda: datetime.date(2024, 3, 5).strftime('%e'))
t('strf_colz', lambda: datetime.datetime(2024, 1, 1, tzinfo=datetime.timezone(datetime.timedelta(hours=5, minutes=30))).strftime('%:z'))
t('strp_G', lambda: datetime.datetime.strptime('2024-01-1', '%G-%V-%u'))
t('strp_sp', lambda: datetime.datetime.strptime('2024 5', '%Y %d'))
t('strp_sp2', lambda: datetime.datetime.strptime(' 5', '%d'))
t('strp_colz', lambda: datetime.datetime.strptime('+05:30', '%z'))
def w(row, **kw):
    s = io.StringIO()
    csv.writer(s, **kw).writerow(row)
    return s.getvalue()
t('csv_empty', lambda: w(['']))
t('csv_empty2', lambda: w(['', '']))
t('csv_nonnum', lambda: w([Decimal('1.5'), Fraction(1, 2), True, 'x', 2], quoting=csv.QUOTE_NONNUMERIC))
t('csv_eof', lambda: list(csv.reader(['a,"b'])))
t('csv_eof_strict', lambda: list(csv.reader(['a,"b'], strict=True)))
t('dec_00', lambda: Decimal(0) / 0)
t('dec_10', lambda: Decimal(1) / 0)
t('dec_pow00', lambda: Decimal(0) ** 0)
t('dec_neg0', lambda: -Decimal('0'))
t('dec_negneg0', lambda: -Decimal('-0'))
t('dec_float_neg0', lambda: float(Decimal('-0')))
t('dec_tuple', lambda: Decimal((1, (1, 4), -1)))
t('dec_floordiv0', lambda: Decimal(0) // 0)
t('dec_mod0', lambda: Decimal(1) % 0)
root = ET.fromstring('<r><a>1</a><b>2</b><a>3</a><b>4</b><c><b>5</b></c></r>')
def tags(xs): return [(e.tag, e.text) for e in xs]
t('et_star1', lambda: tags(root.findall('*[1]')))
t('et_b2', lambda: tags(root.findall('b[2]')))
t('et_last', lambda: tags(root.findall('.//b[last()]')))
t('et_desc2', lambda: tags(root.findall('.//b[2]')))
t('et_dslash', lambda: tags(root.findall('.//')))
t('et_root', lambda: tags(root.findall('/a')))
t('et_zero', lambda: tags(root.findall('a[0]')))
t('et_attr1', lambda: tags(ET.fromstring('<r><a k="1">x</a><a>y</a><a k="2">z</a></r>').findall('a[@k][2]')))
def ins():
    e = ET.Element('r'); e.append(ET.Element('a')); e.append(ET.Element('b'))
    e.insert(-1, ET.Element('c'))
    return [x.tag for x in e]
t('et_insert', ins)
t('fmt_bool', lambda: format(True, '>5'))
t('fmt_bool2', lambda: format(True, ''))
t('fmt_bool3', lambda: format(False, 'd'))
t('fmt_bool4', lambda: f'{True:5}')
t('round_none', lambda: round(2.5, None))
t('round_none2', lambda: round(7, None))
t('int_b', lambda: int(b'12'))
t('int_b2', lambda: int(b' 12 ', 16))
t('float_b', lambda: float(b'1.5'))
t('ord_b', lambda: ord(b'a'))
t('float_ar', lambda: float('١.٥'))
t('int_ar', lambda: int('١٢'))
t('pct_map', lambda: '%(a)s %s' % {'a': 1})
t('pct_list', lambda: 'abc' % [])
t('pct_list2', lambda: 'abc' % ())
t('pct_dict', lambda: 'abc' % {})
t('title_astral', lambda: '\U00010428\U00010429 x'.title())
t('swap_astral', lambda: '\U00010400\U00010428'.swapcase())
class M:
    __p = 1
    def get(self): return self.__p
    def has(self): return hasattr(self, '_M__p')
t('mangle', lambda: (M().get(), M().has()))
t('union_isinst', lambda: isinstance(1, int | str))
t('union_isinst2', lambda: isinstance('a', int | float))
t('slice_hash', lambda: hash(slice(1, 2)) == hash(slice(1, 2)))
t('slice_dict', lambda: {slice(1, 2): 1}[slice(1, 2)])
@dataclasses.dataclass(frozen=True)
class F:
    x: int
def setf():
    F(1).x = 2
t('frozen', setf)
def mdeque():
    match collections.deque([1, 2]):
        case [a, b]: return (a, b)
        case _: return 'no'
t('match_deque', mdeque)
t('re_w_comb', lambda: re.findall(r'\w+', 'é ² Ⅰ'))
t('re_s', lambda: re.findall(r'\s', '\x1c\x1d\x1e\x1f\x85'))
t('re_z', lambda: re.search(r'a\z', 'a') is not None)
t('json_mixed', lambda: json.dumps({1: 'a', 'b': 2}, sort_keys=True))
t('b64_str', lambda: base64.b64encode('abc'))
t('b16_lower', lambda: base64.b16decode('6a'))
t('hmac_nodig', lambda: hmac.new(b'k', b'm').hexdigest())
t('cmp_digest', lambda: hmac.compare_digest('a', b'a'))
t('fromiso_sp', lambda: datetime.date.fromisoformat('2024-01-02 '))
t('parse_qsl_none', lambda: urllib.parse.parse_qsl('a=1', separator=None))
t('b64_pad', lambda: base64.b64decode('YWJj===='))
t('b64_pad2', lambda: base64.b64decode('YWJj=='))
t('intstr', lambda: len(str(10 ** 5000)))
t('hexbig', lambda: len(hex(1 << 20000)))
t('replace_big', lambda: 'abc'.replace('a', 'b', 2 ** 64))
t('fromhex_zeros', lambda: float.fromhex('0x' + '0' * 5000 + '1p0'))
t('dt_strftime_Z', lambda: datetime.datetime(2024, 1, 1, tzinfo=datetime.timezone.utc).strftime('%Z'))
t('hmean_one', lambda: statistics.harmonic_mean([5]))
t('hmean_zero', lambda: statistics.harmonic_mean([0, 1]))
t('log_base1', lambda: math.log(10, 1))
t('floor_str', lambda: math.floor('a'))
t('trunc_str', lambda: math.trunc('a'))
t('ceil_str', lambda: math.ceil('a'))
t('sqrt_ninf', lambda: math.sqrt(-math.inf))
t('log2_frac', lambda: math.log2(Fraction(1, 4)))
t('rb_quote', lambda: rb'a\'b')
t('bad_lookbehind_grp', lambda: re.compile(r'(a)(?<=\1)b'))
t('re_global_late', lambda: re.compile('a(?i)b'))
t('re_range', lambda: re.compile('[z-a]'))
t('re_cat_range', lambda: re.compile(r'[\d-z]'))
t('re_redef', lambda: re.compile('(?P<a>x)(?P<a>y)'))
t('re_unknown_grp', lambda: re.compile('(?P=b)'))
t('re_ext', lambda: re.compile('(?Q)'))
t('re_multi', lambda: re.compile('a\nb)'))
t('re_open_ref', lambda: re.compile(r'(a\1)'))
t('re_octal', lambda: re.compile(r'\777'))
t('re_incomplete', lambda: re.compile(r'\x4'))
t('re_N', lambda: re.compile(r'\N{NOPE}'))
t('re_flags', lambda: re.compile('(?z)'))
t('re_onoff', lambda: re.compile('(?i-i:a)'))
t('re_comment', lambda: re.compile('a(?#x(y)b').pattern)
t('re_comment2', lambda: re.compile('a(?#x').pattern)
def reattrs():
    try:
        re.compile('ab\n(c')
    except re.error as e:
        return (e.msg, e.pos, e.lineno, e.colno, str(e))
t('re_attrs2', reattrs)
for s in ('2024-01-02 ', '2024-01-02T', '2024-01-02 10', '2024-01-02T10:30'):
    t('dt_iso ' + s, lambda: datetime.datetime.fromisoformat(s))
sd = {(1, 2): 'a', (1, 2, 3): 'b', (1,): 'c'}
sa = [1, 2]
t('star_sub', lambda: (sd[*sa], sd[*sa, 3], sd[*[1]], sd[*sa,]))
def star_set():
    sd[*sa] = 'z'
    return sd[1, 2]
t('star_sub_set', star_set)
t('radix_lits', lambda: (0o1337, 0b1011011111, 0x2df, 0b0, 0o0))
