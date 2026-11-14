# Review 5, the answers wanted before a release: DST in one zone, copies of opaque values, asdict's recursion,
# ElementPath's slashes, keywords of random, tee, combine and reduce, Counter's operators, next() and iter() of a
# script iterator, the mapping protocol, match on a namedtuple and a ChainMap, format without a type.
import collections
import copy
import dataclasses
import datetime
import functools
import io
import itertools
import random
import xml.etree.ElementTree as ET
from collections import ChainMap, Counter, OrderedDict, defaultdict, deque, namedtuple
from zoneinfo import ZoneInfo


def t(f):
    try:
        print(repr(f()))
    except BaseException as e:
        print(type(e).__name__, str(e)[:90])


# DST in one zone
ny = ZoneInfo('America/New_York')
print(datetime.datetime(2024, 3, 10, 12, tzinfo=ny) - datetime.datetime(2024, 3, 9, 12, tzinfo=ny))
print(datetime.datetime(2024, 11, 3, 12, tzinfo=ny) - datetime.datetime(2024, 11, 2, 12, tzinfo=ny))
la = ZoneInfo('America/Los_Angeles')
print(datetime.datetime(2024, 3, 10, 12, tzinfo=ny) - datetime.datetime(2024, 3, 9, 12, tzinfo=la))

# copies of opaque values
e = ET.fromstring('<a k="v">t<b>u</b>w<c/></a>')
c = copy.copy(e)
print(c is e, c.tag, c.attrib, c.text, c.tail, len(c), c[0] is e[0], c.attrib is e.attrib)
c.append(ET.Element('z'))
print(len(e), len(c))
d = copy.deepcopy(e)
print(d is e, d.tag, d.attrib, d.attrib is e.attrib, d[0] is e[0], d[0].text, d[0].tail, ET.tostring(d))
d[0].text = 'changed'
print(e[0].text)
tree = ET.ElementTree(e)
print(copy.copy(tree) is tree, copy.copy(tree).getroot() is e, copy.deepcopy(tree).getroot() is e)
print(copy.deepcopy(tree).getroot().tag)
cm = ChainMap({'a': [1]}, {'b': 2})
c2 = copy.copy(cm)
print(c2 is cm, c2.maps[0] is cm.maps[0], c2.maps[1] is cm.maps[1], c2['a'] is cm['a'], dict(c2) == dict(cm))
c3 = copy.deepcopy(cm)
print(c3 is cm, c3.maps[0] is cm.maps[0], c3['a'] is cm['a'], c3['a'], c3['b'])
s = io.StringIO('hello')
s.seek(2)
s2 = copy.copy(s)
print(s2 is s, s2.tell(), s2.read(), s.tell())
s3 = copy.deepcopy(s)
print(s3 is s, s3.getvalue())
b = io.BytesIO(b'hello')
b.seek(3)
b2 = copy.copy(b)
print(b2 is b, b2.tell(), b2.read(), b.tell())
b3 = copy.deepcopy(b)
print(b3 is b, b3.getvalue())
s4 = io.StringIO('x')
s4.close()
t(lambda: copy.copy(s4))
shared = [ET.Element('x')]
pair = copy.deepcopy([shared[0], shared[0]])
print(pair[0] is pair[1], pair[0] is shared[0])


# asdict and astuple
@dataclasses.dataclass
class P:
    x: object = None
    y: object = None


Pt = namedtuple('Pt', 'a b')
inner = [1]
p = P([P(1, 2), (P(3, 4), inner)], {'k': P(5, 6), P: inner})
r = dataclasses.asdict(p)
print(r['x'][0], r['x'][1][0], r['x'][1][1], r['x'][1][1] is inner, r['y']['k'], r['y'][P] is inner)
r2 = dataclasses.astuple(p)
print(r2[0][0], r2[0][1][0], r2[0][1][1] is inner, r2[1]['k'], r2[1][P] is inner)
print(dataclasses.asdict(P(Pt(P(1, 2), 3), 4)), type(dataclasses.asdict(P(Pt(1, 2)))['x']).__name__)
print(dataclasses.astuple(P(Pt(P(1, 2), 3), 4)))
dd = defaultdict(list)
dd['a'].append(P(1, 2))
print(dataclasses.asdict(P(dd))['x'], type(dataclasses.asdict(P(dd))['x']).__name__)
od = OrderedDict(a=P(1, 2))
print(type(dataclasses.asdict(P(od))['x']).__name__, dataclasses.asdict(P(od))['x']['a'])
st = {1, 2}
print(dataclasses.asdict(P(st))['x'] is st, dataclasses.asdict(P(st))['x'])
dq = deque([1])
print(dataclasses.asdict(P(dq))['x'] is dq, dataclasses.asdict(P(dq))['x'])
L = []
L.append(L)
t(lambda: dataclasses.asdict(P(L)))

# ElementPath's slashes
root = ET.fromstring('<r xmlns:x="http://example.org/ns/x"><x:a/><b k="x/y">1</b><b k="x]y">2</b><c><d/></c></r>')
print(len(root.findall('{http://example.org/ns/x}a')), len(root.findall('.//{http://example.org/ns/x}a')))
print([e.text for e in root.findall("b[@k='x/y']")], [e.text for e in root.findall('b[@k="x/y"]')])
print([e.text for e in root.findall("b[@k='x]y']")], len(root.findall('c/d')), len(root.findall('.//d')))
print(root.find("{http://example.org/ns/x}a") is not None, root.findtext("b[@k='x/y']"))

# keywords of random
random.seed(a=7)
x1 = random.random()
random.seed(7)
print(x1 == random.random())
t(lambda: random.seed(a=7, foo=1))
random.seed(1)
print(all(90 < random.gauss(mu=100, sigma=1) < 110 for _ in range(50)))
print(all(90 < random.normalvariate(mu=100, sigma=1) < 110 for _ in range(50)))
print(all(90 < random.gauss(100, sigma=1) < 110 for _ in range(50)))
print(all(-20 < random.gauss(sigma=1) < 20 for _ in range(50)))
print(all(10 <= random.triangular(low=10, high=20) <= 20 for _ in range(50)))
print(all(10 <= random.triangular(10, high=20, mode=11) <= 20 for _ in range(50)))
print(all(5 <= random.randint(a=5, b=6) <= 6 for _ in range(50)))
print(all(5 <= random.uniform(a=5, b=6) <= 6 for _ in range(50)))
print(all(0 <= random.randrange(start=3) < 3 for _ in range(50)))
print(all(5 <= random.randrange(5, stop=7) < 7 for _ in range(50)))
print(all(random.randrange(0, 9, step=2) % 2 == 0 for _ in range(50)))
print(random.choice(seq=[1]), len(random.randbytes(n=3)), 0 <= random.getrandbits(3) < 8)
print(random.expovariate() > 0, random.expovariate(lambd=2.0) > 0, random.gammavariate(alpha=1, beta=1) > 0)
print(0 <= random.betavariate(alpha=1, beta=1) <= 1)
t(lambda: random.gauss(foo=1))
t(lambda: random.randint(a=1))
t(lambda: random.uniform(1, a=2))

# tee, combine, reduce, deepcopy's memo
t(lambda: len(itertools.tee(range(3), n=3)))
t(lambda: len(itertools.tee(range(3), 3)))
day = datetime.date(2024, 1, 2)
tm = datetime.time(3, 4, tzinfo=datetime.timezone.utc, fold=1)
print(datetime.datetime.combine(day, tm), datetime.datetime.combine(day, tm).fold)
print(datetime.datetime.combine(day, datetime.time(3), tzinfo=datetime.timezone.utc))
print(datetime.datetime.combine(day, datetime.time(3), datetime.timezone.utc))
print(datetime.datetime.combine(day, tm, tzinfo=None))
print(datetime.datetime.combine(date=day, time=datetime.time(3)))
t(lambda: datetime.datetime.combine(day))
t(lambda: datetime.datetime.combine(day, tm, foo=1))
print(datetime.datetime.fromtimestamp(timestamp=0, tz=datetime.timezone.utc))
t(lambda: datetime.datetime.strptime('2024', format='%Y'))
t(lambda: datetime.datetime.strptime('2024', '%Y'))
print(functools.reduce(lambda a, b: a + b, [1, 2, 3], initial=10))
print(functools.reduce(lambda a, b: a + b, [1, 2, 3], 10))
t(lambda: functools.reduce(lambda a, b: a + b, [1], foo=1))
t(lambda: functools.reduce(lambda a, b: a + b, [1], 1, initial=2))
t(lambda: functools.reduce(lambda a, b: a + b, [], initial=None))
m = {}
print(copy.deepcopy([1], memo=m), copy.deepcopy([1], m))
t(lambda: copy.deepcopy([1], foo=1))

# Counter
print(Counter(a=3) == Counter(a=3, b=0), Counter(a=3) != Counter(a=3, b=0), Counter(a=3) == Counter(a=2), Counter() == Counter(a=0))
print(Counter(a=3) == {'a': 3}, Counter(a=3, b=0) == {'a': 3}, {'a': 3} == Counter(a=3, b=0))
c = Counter(a=3)
del c['zz']
print(c)
c = Counter(a=3, c=1)
y = c
c |= Counter(a=1, b=2)
print(c, y is c)
c = Counter(a=3, c=1)
y = c
c &= Counter(a=1, b=2)
print(c, y is c)
c = Counter(a=3, n=-1)
y = c
c += Counter(a=1, b=2, z=-2)
print(c, y is c)
c = Counter(a=3, n=-1)
y = c
c -= Counter(a=1, b=2, z=-2)
print(c, y is c)
c = Counter(a=1)
c += {'b': 2}
print(c)
c = Counter(a=1)
try:
    c += 1
except AttributeError as ex:
    print('AttributeError', ex)
try:
    c &= {'x': 1}
except KeyError as ex:
    print('KeyError', ex)
print(Counter(ChainMap({'a': 2}, {'b': 3})))
c = Counter()
c.update(ChainMap({'a': 2}))
print(c)
c = Counter(a=2, b=1)
print(c.most_common(1), c.most_common(), c.most_common(None), c.most_common(-1), c.most_common(0))
t(lambda: c.most_common(1.5))
t(lambda: c.most_common('x'))


# next() and iter() of a script iterator
class It:
    def __init__(self):
        self.n = 0

    def __iter__(self):
        return self

    def __next__(self):
        self.n += 1
        if self.n > 2:
            raise StopIteration('rec')
        return self.n


i = It()
print(iter(i) is i, next(i), next(i))
t(lambda: next(i))
print(next(i, 'dflt'))
print(list(It()), [x for x in It()], sum(It()), next(It()))


class OnlyNext:
    def __next__(self):
        return 1


print(next(OnlyNext()))
t(lambda: iter(OnlyNext()))
t(lambda: next([]))


class Other:
    def __iter__(self):
        return iter([1, 2])


print(list(Other()), next(iter(Other())))
t(lambda: iter(Other()) is Other())


class BadIter:
    def __iter__(self):
        return 1


t(lambda: iter(BadIter()))


# the mapping protocol
class M:
    def keys(self):
        return ['a', 'b']

    def __getitem__(self, k):
        return k * 2


print(dict(M()), {**M()}, OrderedDict(M()), defaultdict(int, M())['a'])
d = {}
d.update(M())
print(d)
d = {'z': 1}
d |= M()
print(d)


class Bad:
    keys = 1


t(lambda: dict(Bad()))
print(dict(ChainMap({'a': 1})), {**ChainMap({'a': 1})}, dict(M(), b=1))

# match on a namedtuple and a ChainMap
Point = namedtuple('Point', 'x y')


def m1(v):
    match v:
        case Point(x, y) if x > 5:
            return ('big', x, y)
        case Point(x=1, y=b):
            return ('kw', b)
        case Point(a):
            return ('one', a)
        case {'a': w, **rest}:
            return ('map', w, rest)
        case _:
            return 'none'


print(m1(Point(9, 2)), m1(Point(1, 2)), m1(Point(3, 2)), m1((9, 2)), m1(ChainMap({'a': 1, 'c': 3}, {'b': 2})), m1(OrderedDict(a=1)))
print(Point.__match_args__)


def m2(v):
    match v:
        case Point(a, b, c):
            return 1


t(lambda: m2(Point(1, 2)))

# format without a type
print(format(1.0, '.3'), format(2.0, '.2'), f"{2.0:.2}", format(123.0, '.3'), format(100.0, '.3'), format(1e10, '.3'), format(1.5, '.3'))
print(format(0.0, '.3'), format(1.0, '.1'), format(12.0, '.2'), format(10.0, '.1'), format(0.1, '.1'), format(-1.0, '.3'), format(1e-5, '.3'))
print(format(1.0, '10.3'), format(1.0, '<10.3') + '|', format(1234.0, ',.5'), format(1e100, '.3'), format(float('inf'), '.3'), format(1.0, '#.3'))
print('{:.3}'.format(1.0), '%.3g' % 1.0, format(1.0, '.3g'), format(1.0, ''), format(100000.0, '.6'), format(1000000.0, '.6'))
