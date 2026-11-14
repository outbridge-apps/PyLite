from operator import itemgetter, attrgetter, methodcaller
class O:
    def __init__(self):
        self.x = 1
        self.y = self
    def m(self, *a, **k):
        return (a, k)
o = O()
ig, ag, mc = itemgetter(1), attrgetter("x", "y.x"), methodcaller("m", 1, k=2)
for f, args, kw in [(ig, (), {}), (ig, ([1, 2], [3]), {}), (ig, ([1, 2],), {"a": 1}), (ig, ([1, 2],), {}),
                    (ag, (), {}), (ag, (o, o), {}), (ag, (o,), {"q": 1}), (ag, (o,), {}),
                    (mc, (), {}), (mc, (o, o), {}), (mc, (o,), {"z": 1}), (mc, (o,), {}),
                    (itemgetter(0, 1), ("ab",), {}), (attrgetter("y.y.y.x"), (o,), {})]:
    try:
        print(f(*args, **kw))
    except Exception as e:
        print(type(e).__name__, e)
try:
    attrgetter("")(o)
except Exception as e:
    print(type(e).__name__, e)
try:
    attrgetter("x..y")(o)
except Exception as e:
    print(type(e).__name__, e)
rows = [(i, -i, str(i)) for i in range(200000)]
g = itemgetter(1)
s = 0
for r in rows:
    s += g(r)
print(s, sorted(rows, key=itemgetter(2))[:2], max(rows, key=g)[0])
