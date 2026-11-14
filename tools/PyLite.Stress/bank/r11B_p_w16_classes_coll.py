import time, itertools, functools, operator
from collections import namedtuple, OrderedDict, deque, Counter, defaultdict
from dataclasses import dataclass, field
t = time.perf_counter()

class Account:
    __slots__ = ("no", "bal", "hist")
    def __init__(self, no):
        self.no = no
        self.bal = 0
        self.hist = []
    def post(self, amt):
        if self.bal + amt < -1000:
            raise ValueError("overdraft %s" % self.no)
        self.bal += amt
        self.hist.append(amt)
    def __repr__(self):
        return "Account(%r, %r)" % (self.no, self.bal)

class Savings(Account):
    __slots__ = ()
    def post(self, amt):
        super().post(amt)

accs = {i: (Savings(i) if i % 3 == 0 else Account(i)) for i in range(500)}
fails = 0
for i in range(60000):
    try:
        accs[i % 500].post((i * 37) % 200 - 110)
    except ValueError:
        fails += 1
t1 = time.perf_counter()

@dataclass(order=True)
class Item:
    sku: str
    qty: int = 0
    tags: list = field(default_factory=list)

items = [Item("S%05d" % ((i * 7919) % 10000), i % 13) for i in range(10000)]
items.sort()
items2 = sorted(items, key=lambda x: (-x.qty, x.sku))
P = namedtuple("P", "x y z")
pts = [P(i, i * 2, i % 5) for i in range(20000)]
by = defaultdict(list)
for p in pts:
    by[p.z].append(p)
srt = sorted(pts, key=operator.attrgetter("z", "x"), reverse=True)
t2 = time.perf_counter()
grp = {k: sum(1 for _ in g) for k, g in itertools.groupby(sorted(p.z for p in pts))}
chunks = list(itertools.islice(itertools.batched(range(100000), 7), 5000)) if hasattr(itertools, "batched") else []
comb = sum(1 for _ in itertools.combinations(range(60), 3))
prod = sum(a * b for a, b in itertools.product(range(200), range(200)))
acc = list(itertools.accumulate(range(50000)))
red = functools.reduce(operator.add, range(100000))
dq = deque(maxlen=100)
for i in range(100000):
    dq.append(i)
cnt = Counter("".join(str(i) for i in range(30000)))
od = OrderedDict((str(i), i) for i in range(20000))
od.move_to_end("5")
sq = {x: x * x for x in range(50000) if x % 3}
nest = [[y for y in range(x % 10)] for x in range(20000)]
t3 = time.perf_counter()
print(fails, repr(accs[3]), items[0].sku, items2[0].sku, srt[0], len(by[0]), grp, len(chunks), comb, prod, acc[-1], red,
      dq[0], cnt.most_common(2), next(reversed(od)), len(sq), sum(map(len, nest)))
print([round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3))])
