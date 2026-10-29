from collections import namedtuple

out = []
P = namedtuple('P', 'x y')
p = P(1, y=2)
out.append(repr(p))
out.append((p.x, p[1], p._fields, tuple(p)))
out.append(repr(p._replace(x=10)))
out.append(sorted(p._asdict().items()))
out.append(repr(P._make([5, 6])))
a, b = p
out.append(a + b)
out.append(p == (1, 2))
Q = namedtuple('Q', ['name', 'score'])
rows = [Q('b', 2), Q('a', 3), Q('c', 1)]
out.append([q.name for q in sorted(rows, key=lambda q: q.score)])
R = namedtuple('R', 'a def class', rename=True)
out.append(R._fields)
try:
    namedtuple('Bad', 'a a')
    out.append('no error')
except ValueError:
    out.append('ValueError')
out.append(isinstance(p, tuple))
result = out
