# repeat: 2
import re, json, collections, dataclasses, enum, functools, copy, decimal, datetime, string, typing, csv, io
from xml.etree import ElementTree as ET

P = collections.namedtuple("P", "x y z")
class E(Exception):
    pass
class Color(enum.Enum):
    RED = 1
    GREEN = 2
@dataclasses.dataclass
class D:
    a: int
    b: str = "x"
class K:
    def __init__(self, v):
        self.v = v
    def m(self, w):
        return self.v + w

@functools.lru_cache(maxsize=64)
def fib(n):
    return n if n < 2 else fib(n - 1) + fib(n - 2)

acc = 0
for i in range(300):
    p = P(i, i + 1, i + 2)
    acc += p._replace(x=1).x + p.count(1) + p.index(i + 1)
    try:
        raise E("bad %d" % i)
    except E as e:
        e.add_note("n")
        acc += len(e.args)
    acc += K(i).m(1)
    acc += D(i).a
    acc += Color(1).value
    acc += id(p) & 1
m = copy.deepcopy({"a": [1, 2, {"b": 3}]}, {})
acc += fib(60) % 7
t = string.Template("$a-$b").substitute(a=1, b=2)
acc += len(re.findall(r"\b\w+\b", "hello world é ß " * 50))
acc += len(re.sub(r"(\d)", r"<\1>", "a1b2c3" * 30))
acc += len(json.dumps(json.loads('{"a":[1,2,3],"b":{"c":"d"}}')))
q = decimal.Decimal("1.10") * 3
with decimal.localcontext() as ctx:
    ctx.prec = 5
    q = q / 7
dt = datetime.datetime(2024, 1, 1) + datetime.timedelta(days=3)
acc += len(dt.isoformat())
root = ET.fromstring("<a><b x='1'/><b x='2'/></a>")
acc += len(root.findall("b"))
buf = io.StringIO()
w = csv.writer(buf)
w.writerow([1, "a,b", 3])
acc += len(buf.getvalue())
T = typing.NewType("T", int)
acc += T(3)
print(acc, t, q)
