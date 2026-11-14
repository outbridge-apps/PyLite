def t(f):
    try:
        print(repr(f()))
    except Exception as e:
        print('EXC', type(e).__name__ + ':', e)
# W1
t(lambda: sum([0.1]*10))
t(lambda: sum([3.3, 1.1, 2.2]))
t(lambda: sum([1, 2, 0.5, 3]))
t(lambda: sum([-0.0]))
t(lambda: sum([-0.0], -0.0))
t(lambda: sum([float('inf'), 1.0]))
t(lambda: sum([1e308, 1e308, -1e308]))
t(lambda: sum([0.1, 0.2, 2**70, 0.3]))
t(lambda: sum([0.1, True, 0.2]))
t(lambda: sum([1e100, 1.0, -1e100, 1.0]))
t(lambda: sum([2**62, 2**62, 2**62]))
t(lambda: sum([1.5, 'a']))
t(lambda: sum([[1], [2]], []))
t(lambda: sum(x * 0.1 for x in range(100)))
t(lambda: sum([0.1] * 10, 1))
t(lambda: sum([0.1] * 3, 0.0))
from decimal import Decimal
from fractions import Fraction
t(lambda: sum([0.5, Fraction(1, 3)]))
t(lambda: sum([Decimal('0.1')] * 3))
# W11
import statistics
t(lambda: statistics.stdev([0, 10**200]))
t(lambda: statistics.stdev([13, 43, 32, 48]))
t(lambda: statistics.pstdev([13, 43, 32, 48]))
t(lambda: statistics.stdev([1.5, 2.5, 2.5, 2.75, 3.25, 4.75]))
t(lambda: statistics.stdev([Fraction(1, 3), Fraction(2, 3), Fraction(5, 7)]))
t(lambda: statistics.stdev([Decimal('1.5'), Decimal('2.5'), Decimal('4.75')]))
t(lambda: statistics.stdev([1]))
t(lambda: statistics.pstdev([]))
t(lambda: statistics.stdev([float('nan'), 1]))
t(lambda: statistics.stdev([5, 5, 5]))
t(lambda: statistics.stdev([1e-300, 3e-300]))
import random
random.seed(5)
bad = 0
for _ in range(300):
    d = [random.randint(0, 100) for _ in range(random.randint(2, 7))]
    bad += statistics.stdev(d) != statistics.stdev(d)
print('ok', bad)
# W12
def boom(x):
    raise StopIteration
t(lambda: [boom(x) for x in range(3)])
t(lambda: list(boom(x) for x in range(3)))
it = iter([1, 2])
try:
    r = [next(it) for _ in range(3)]
except StopIteration:
    print('stop')
# W5
rows = [{'qty': 1}, {'qty': 7}, {'qty': 9}]
found = None
print(any((found := r)['qty'] > 4 for r in rows), found)
xs = [1, 2, 3]
print([last := x for x in xs], last)
total = 0
print([total := total + x for x in xs], total)
def f():
    tot = 0
    vals = [tot := tot + v for v in [5, 6]]
    return vals, tot
print(f())
def g():
    return [y for x in range(3) if (y := x * 2) > 1], y
print(g())
def h():
    def inner():
        return [(z := i) for i in range(4)]
    a = inner()
    return a
print(h())
print([[(w := j) for j in range(2)] for i in range(2)], w)
# W6
import datetime, collections, io, uuid, zoneinfo, xml.etree.ElementTree as ET, csv
print(type(datetime.date(2020, 1, 1)) is datetime.date, type(datetime.datetime(2020, 1, 1)) is datetime.datetime)
print(type(datetime.timedelta(1)) is datetime.timedelta, type(datetime.time(1)) is datetime.time)
print(type(Decimal(1)) is Decimal, type(Fraction(1)) is Fraction)
print(type(collections.Counter()) is collections.Counter, type(collections.OrderedDict()) is collections.OrderedDict)
print(type(collections.defaultdict(int)) is collections.defaultdict, type(collections.deque()) is collections.deque)
print(type(io.StringIO()) is io.StringIO, type(io.BytesIO()) is io.BytesIO)
print(type(uuid.uuid4()) is uuid.UUID, type(zoneinfo.ZoneInfo('UTC')) is zoneinfo.ZoneInfo)
print(isinstance(ET.Element('a'), ET.Element), type(ET.Element('a')) is ET.Element, type(csv.Sniffer()) is csv.Sniffer)
print(type(datetime.timezone.utc) is datetime.timezone, type(datetime.date) is type)
P = collections.namedtuple('P', 'a b')
print(type(P(1, 2)) is P)
print(ET.Element, ET.Element.__name__, csv.Sniffer)
