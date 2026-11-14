# repeat: 2
import collections
t = 0
for k in range(300):
    P = collections.namedtuple("P%d" % k, "a b c d e f")
    p = P(1, 2, 3, 4, 5, 6)
    t += p.count(1) + p.index(2) + len(p._asdict())
class E(Exception):
    pass
for k in range(300):
    e = E(k)
    e.add_note("x")
    e.with_traceback(None)
print(t)
