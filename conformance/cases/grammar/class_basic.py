# Adapted from CPython's test suite (class definition basics, Lib/test/test_class.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

class Point:
    def __init__(self, x, y):
        self.x = x
        self.y = y

    def dist2(self):
        return self.x * self.x + self.y * self.y

    def shifted(self, dx):
        return Point(self.x + dx, self.y)

p = Point(3, 4)
assert p.x == 3 and p.y == 4
assert p.dist2() == 25
q = p.shifted(1)
assert q.x == 4 and p.x == 3      # a new instance, the original untouched

# attribute mutation
p.x = 30
assert p.dist2() == 916

# instances in containers
pts = [Point(1, 0), Point(0, 2)]
assert [pt.dist2() for pt in pts] == [1, 4]

# custom __eq__ participates in ==
class Tag:
    def __init__(self, name):
        self.name = name

    def __eq__(self, other):
        return isinstance(other, Tag) and self.name == other.name

assert Tag('a') == Tag('a')
assert Tag('a') != Tag('b')       # != is the inversion of __eq__ in this dialect
assert not (Tag('a') == 'a')
