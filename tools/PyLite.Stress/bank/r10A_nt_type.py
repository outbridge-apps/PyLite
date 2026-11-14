from collections import namedtuple
import typing
from typing import NamedTuple, TypedDict
P = namedtuple("P", "x y", defaults=[5])
p = P(1)
print(type(p) is P, isinstance(p, P), type(p).__name__, repr(type(p)), p, p._fields, P._field_defaults)
print(P._make([3, 4]), type(p)._make([7, 8]), p._replace(x=9), p._asdict())
print(type(p)(10, 20), P.__name__, P._fields)
Q = NamedTuple("Q", [("a", int), ("b", str)])
q = Q(1, "z")
print(type(q) is Q, isinstance(q, Q), isinstance(q, tuple), q, Q._fields, type(q)(2, "w"))
TD = TypedDict("TD", {"a": int})
d = TD(a=1)
print(type(d) is dict, d, TD.__name__, repr(TD))
try:
    isinstance(d, TD)
except TypeError as e:
    print("TE", e)
A1 = namedtuple("Same", "a")
A2 = namedtuple("Same", "b")
a1 = A1(1); a2 = A2(2)
print(type(a1) is A1, type(a2) is A2, type(a1) is type(a2), a1, a2)
import copy
print(copy.copy(p), copy.deepcopy(q), type(copy.copy(p)) is P, type(copy.deepcopy(q)) is Q)
match p:
    case P(x, y):
        print("matched", x, y)
s = {p, P(1, 5)}
print(len(s), p == (1, 5))
print(p.count(1), p.index(5), p[0], p[-1], p[:1])
def f():
    R = namedtuple("R", "v")
    return R(1)
rs = [f() for _ in range(3)]
print(rs, [type(r).__name__ for r in rs], type(rs[0]) is type(rs[1]))
print(issubclass(P, tuple), str(P))
import json
print(json.dumps(p), json.dumps(d), sorted([P(2, 1), P(1, 2)]))
print(type(P), type(TD), type(Q))
