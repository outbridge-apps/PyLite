# Adapted from CPython's test suite (Lib/test/test_dataclasses.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from dataclasses import dataclass, field, asdict

@dataclass
class Point:
    x: int
    y: int = 0

# generated __init__ with defaults
p = Point(1, 2)
assert p.x == 1 and p.y == 2
q = Point(5)
assert q.y == 0

# generated __eq__ compares by fields
assert Point(1, 2) == Point(1, 2)
assert Point(1, 2) != Point(1, 3)

# mutation
p.x = 10
assert p.x == 10

# asdict
assert asdict(Point(1, 2)) == {'x': 1, 'y': 2}

# default_factory for mutable defaults
@dataclass
class Box:
    items: list = field(default_factory=list)

b1 = Box()
b2 = Box()
b1.items.append('a')
assert b1.items == ['a']
assert b2.items == []          # each instance gets a FRESH list

# nested dataclasses in containers
pts = [Point(2, 1), Point(1, 1)]
assert sorted(pts, key=lambda t: t.x)[0] == Point(1, 1)
