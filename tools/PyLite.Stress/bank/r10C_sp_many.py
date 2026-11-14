# repeat: 2
# deadline: 120000
# allocmb: 3000
import time, json, csv, io, re, datetime, decimal, collections, itertools, functools, math, statistics, string, textwrap
import hashlib, base64, struct, operator, heapq, bisect, copy, fractions, random, unicodedata, urllib.parse, enum, dataclasses, typing
from xml.etree import ElementTree as ET
T = time.perf_counter
out = []
def sec(name, f):
    t = T()
    f()
    out.append("%s=%d" % (name, (T() - t) * 1000))

TEXT = " ".join("Word%d foo-%d bar_%d 2024-01-%02d x@y.com.example.com" % (i, i, i, i % 28 + 1) for i in range(2000))
D = decimal.Decimal
P = collections.namedtuple("P", "a b c")
class Col(enum.Enum):
    R = 1
    G = 2
@dataclasses.dataclass
class DC:
    a: int
    b: str
    c: float = 0.0

def b(n):
    return range(n)

tests = [
    ("str_split", lambda: [s.split() for s in [TEXT] * 30]),
    ("str_replace", lambda: [TEXT.replace("foo", "baz") for _ in b(50)]),
    ("str_join", lambda: [",".join(str(i) for i in b(20000)) for _ in b(3)]),
    ("str_format", lambda: ["{0}-{1:.3f}-{2:>8}".format(i, i / 3, "x") for i in b(30000)]),
    ("fstring", lambda: [f"{i}:{i * 2:05d}:{i / 7:.2f}" for i in b(30000)]),
    ("percent", lambda: ["%s=%d (%.2f)" % ("k", i, i / 3) for i in b(30000)]),
    ("str_methods", lambda: [(s.lower(), s.upper(), s.title(), s.strip(), s.startswith("W")) for s in TEXT.split()[:8000]]),
    ("str_find", lambda: [TEXT.find("Word1999"), TEXT.count("foo"), TEXT.rfind("x@")] * 50),
    ("str_in", lambda: sum(1 for i in b(20000) if ("Word%d" % i) in TEXT[:5000])),
    ("str_cmp_sort", lambda: sorted(TEXT.split())),
    ("str_isdigit", lambda: [w.isdigit() or w.isalpha() or w.isalnum() for w in TEXT.split()] * 3),
    ("str_encode", lambda: [TEXT.encode("utf-8").decode("utf-8") for _ in b(30)]),
    ("list_ops", lambda: [list(range(1000))[::-1].index(5) for _ in b(300)]),
    ("list_sort_key", lambda: sorted(range(50000), key=lambda v: (v % 13, -v))),
    ("list_comp", lambda: [[x * y for x in range(30)] for y in range(2000)]),
    ("dict_build", lambda: [{str(i): i for i in range(2000)} for _ in b(10)]),
    ("dict_ops", lambda: [collections.Counter(TEXT.split()).most_common(5) for _ in b(5)]),
    ("defaultdict", lambda: _dd()),
    ("set_ops", lambda: [set(range(i, i + 2000)) & set(range(1000, 3000)) for i in b(100)]),
    ("tuple_hash", lambda: len({(i, i % 7, str(i)) for i in b(30000)})),
    ("namedtuple", lambda: [P(i, i, i)._replace(a=1).b for i in b(20000)]),
    ("dataclass", lambda: [DC(i, "x") == DC(i, "x") for i in b(10000)]),
    ("enum", lambda: [Col(1) is Col.R and Col["G"].value for _ in b(20000)]),
    ("int_arith", lambda: sum((i * 7 + 3) % 11 for i in b(200000))),
    ("bigint", lambda: [str(3 ** 5000) for _ in b(20)]),
    ("float_math", lambda: sum(math.sqrt(i) * math.sin(i) + math.log(i + 1) for i in b(100000))),
    ("float_repr", lambda: [repr(i / 7) for i in b(30000)]),
    ("round", lambda: [round(i / 7, 3) for i in b(50000)]),
    ("decimal", lambda: [(D(i) / D(7)).quantize(D("0.001")) for i in b(10000)]),
    ("decimal_str", lambda: [str(D("%d.%03d" % (i, i % 1000)) * D("1.5")) for i in b(10000)]),
    ("fraction", lambda: sum(fractions.Fraction(i, i + 1) for i in range(1, 2000))),
    ("statistics", lambda: [statistics.median(range(i, i + 500)) + statistics.stdev(range(i, i + 50)) for i in b(100)]),
    ("json_dumps", lambda: [json.dumps({"a": list(range(200)), "b": {"c": "d" * 20}, "e": [1.5, None, True]}) for _ in b(500)]),
    ("json_loads", lambda: [json.loads('{"a":[1,2,3,4,5,6,7,8,9],"b":{"c":"ddddd","e":[1.5,null,true]},"f":"x\\u00e9y"}') for _ in b(5000)]),
    ("csv_write", lambda: csv.writer(io.StringIO()).writerows([[i, "a b", 'q"', 1.5] for i in b(10000)])),
    ("csv_read", lambda: list(csv.reader(io.StringIO("1,abc,\"q,x\",4.5\n" * 10000)))),
    ("re_findall", lambda: [re.findall(r"\w+@\w+\.com", TEXT) for _ in b(20)]),
    ("re_sub", lambda: [re.sub(r"(\d+)", lambda m: m.group(1)[::-1], TEXT) for _ in b(5)]),
    ("re_match_loop", lambda: [re.match(r"[A-Z]\w*\d", w) for w in TEXT.split()] * 2),
    ("re_ignorecase", lambda: [re.findall(r"(?i)word\d+", TEXT) for _ in b(10)]),
    ("re_split", lambda: [re.split(r"[\s_-]+", TEXT) for _ in b(10)]),
    ("dt_arith", lambda: [datetime.datetime(2024, 1, 1) + datetime.timedelta(minutes=i) for i in b(30000)]),
    ("dt_strftime", lambda: [datetime.datetime(2024, 1, 1, i % 24).strftime("%Y-%m-%d %H:%M:%S %a %b") for i in b(10000)]),
    ("dt_strptime", lambda: [datetime.datetime.strptime("2024-05-%02d 11:22:33" % (i % 28 + 1), "%Y-%m-%d %H:%M:%S") for i in b(5000)]),
    ("dt_iso", lambda: [datetime.datetime.fromisoformat("2024-05-01T10:20:30.123456+02:00").isoformat() for _ in b(10000)]),
    ("date_ops", lambda: [datetime.date(2024, 1, 1).toordinal() + datetime.date.fromordinal(738000 + i).weekday() for i in b(20000)]),
    ("xml_build", lambda: ET.tostring(functools.reduce(lambda r, i: (ET.SubElement(r, "e", a=str(i)), r)[1], range(10000), ET.Element("r")))),
    ("xml_parse", lambda: [ET.fromstring("<r>" + "<e a='1'>t</e>" * 2000 + "</r>") for _ in b(5)]),
    ("xml_findall", lambda: [ET.fromstring("<r>" + "<e a='1'><f/></e>" * 2000 + "</r>").findall(".//f") for _ in b(5)]),
    ("itertools", lambda: [list(itertools.islice(itertools.cycle("abc"), 1000)) for _ in b(50)] + list(itertools.permutations(range(7)))),
    ("groupby", lambda: [(k, len(list(g))) for k, g in itertools.groupby(sorted(TEXT.split(), key=len), key=len)]),
    ("functools", lambda: [functools.reduce(operator.add, range(1000)) for _ in b(100)]),
    ("lru", lambda: [functools.lru_cache(maxsize=128)(lambda v: v * 2)(i % 200) for i in b(5000)]),
    ("closures", lambda: [(lambda a: (lambda b2: a + b2))(i)(1) for i in b(50000)]),
    ("exceptions", lambda: [int(s) if s.isdigit() else 0 for s in TEXT.split()] and sum(1 for i in b(20000) if _exc(i))),
    ("generators", lambda: sum(sum(x for x in range(100)) for _ in b(1000))),
    ("hashlib", lambda: [hashlib.md5(TEXT.encode()).hexdigest() for _ in b(50)]),
    ("base64", lambda: [base64.b64decode(base64.b64encode(TEXT.encode())) for _ in b(50)]),
    ("struct", lambda: [struct.unpack("<iqd", struct.pack("<iqd", i, i, i)) for i in b(20000)]),
    ("heapq", lambda: heapq.nlargest(10, range(100000))),
    ("bisect", lambda: [bisect.insort(L, i % 997) for L in [[]] for i in b(20000)]),
    ("copy", lambda: [copy.deepcopy({"a": [1, 2, {"b": (3, 4)}], "c": "d"}) for _ in b(5000)]),
    ("random", lambda: [random.randint(1, 100) + random.random() for _ in b(50000)]),
    ("unicodedata", lambda: [unicodedata.normalize("NFC", "é" * 50) for _ in b(2000)]),
    ("urllib", lambda: [urllib.parse.quote("a b&c=d/é") + urllib.parse.urlencode({"a": i}) for i in b(10000)]),
    ("textwrap", lambda: [textwrap.wrap(TEXT[:3000], 40) for _ in b(10)]),
    ("template", lambda: [string.Template("$a and $b").substitute(a=i, b=2) for i in b(5000)]),
    ("typing", lambda: [typing.List[int] for _ in b(5000)]),
    ("class_attr", lambda: _cls_loop()),
]

def _dd():
    d = collections.defaultdict(int)
    for i in range(20000):
        d[i % 300] += 1
    return d

def _exc(i):
    try:
        if i % 3 == 0:
            raise KeyError(i)
        return False
    except KeyError:
        return True

class Base:
    k = 1
    def __init__(self, v):
        self.v = v
    def get(self):
        return self.v + self.k
class Sub(Base):
    def get(self):
        return super().get() * 2

def _cls_loop():
    t = 0
    for i in range(30000):
        o = Sub(i)
        t += o.get() + o.v
    return t

for name, f in tests:
    sec(name, f)
print(" ".join(out))
