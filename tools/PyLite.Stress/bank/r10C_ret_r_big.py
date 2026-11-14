# repeat: 2
import re, collections, json
P = collections.namedtuple("P", "a b")
big = [P(i, str(i)) for i in range(150000)]
t = 0
for p in big[:50000]:
    t += p.count(1)
class E(Exception):
    pass
try:
    raise E(big)
except E as e:
    e.add_note("x" * 100000)
s = json.dumps([list(p) for p in big[:50000]])
m = re.search("9999", s)
print(t, len(s), m.start())
