# Adapted from CPython's test suite (Lib/test/test_collections.py namedtuple cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import namedtuple

Point = namedtuple('Point', ['x', 'y'])

# construction and access by name AND index
p = Point(3, 4)
assert p.x == 3 and p.y == 4
assert p[0] == 3 and p[1] == 4
assert len(p) == 2

# it compares and unpacks as a tuple (isinstance(p, tuple) is a documented
# deviation — see cases/deviations/isinstance_host_containers.py)
assert p == (3, 4)
a, b = p
assert (a, b) == (3, 4)

# keyword construction
q = Point(y=2, x=1)
assert q == (1, 2)

# _fields and _make
assert Point._fields == ('x', 'y')
assert Point._make([10, 20]) == Point(10, 20)

# field-name space form
Pair = namedtuple('Pair', 'left right')
assert Pair(1, 2).right == 2

# usable in sets/dicts and sortable like tuples
pts = sorted([Point(2, 1), Point(1, 9)])
assert pts[0] == Point(1, 9)
assert Point(1, 2) in {Point(1, 2)}

# defaults= (3.7+) fill the rightmost fields; _field_defaults names them
P = namedtuple("P", "x y z", defaults=(5, 6))
assert P(1) == (1, 5, 6) and P(1, 2) == (1, 2, 6) and P(1, z=9) == (1, 5, 9) and P._field_defaults == {"y": 5, "z": 6}
assert namedtuple("Q", "a b")._field_defaults == {}
try:
    namedtuple("R", "a", defaults=[1, 2])
    assert False
except TypeError:
    pass
