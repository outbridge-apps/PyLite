# Adapted from CPython's test suite (Lib/test/test_tuple.py, test_set.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# tuples: construction, indexing, singleton syntax
t = (1, 2, 3)
assert t[0] == 1 and t[-1] == 3
assert t[1:] == (2, 3)
assert (1,) != (1)                  # (1) is just the int 1
assert len(()) == 0
assert (1, 2) + (3,) == (1, 2, 3)
assert (0,) * 3 == (0, 0, 0)
assert (1, 2) < (1, 3)
assert tuple([1, 2]) == (1, 2)
assert tuple('ab') == ('a', 'b')

# immutability
raised = False
try:
    t[0] = 99
except TypeError:
    raised = True
assert raised

# tuples as dict keys
routes = {(1, 2): 'a', (1, 3): 'b'}
assert routes[(1, 2)] == 'a'

# nested unpacking
(a, (b, c)) = (1, (2, 3))
assert a + b + c == 6

# count/index
u = (1, 2, 2, 3)
assert u.count(2) == 2
assert u.index(3) == 3

# sets: dedupe, membership, methods
s = {1, 2, 2, 3}
assert len(s) == 3
assert 2 in s and 5 not in s
assert s == {3, 2, 1}               # order-insensitive equality
assert set('aab') == {'a', 'b'}
s.add(4)
s.add(4)
assert len(s) == 4
s.discard(99)                        # discard on a missing element is silent
s.remove(4)
raised = False
try:
    s.remove(99)                     # remove on a missing element raises
except KeyError:
    raised = True
assert raised

# algebra
x = {1, 2, 3}
y = {3, 4}
assert x | y == {1, 2, 3, 4}
assert x & y == {3}
assert x - y == {1, 2}
assert x ^ y == {1, 2, 4}
assert {1, 2} <= {1, 2, 3}
assert {1, 2, 3} >= {1, 2}
assert not ({1, 5} <= {1, 2, 3})

# set comprehension
assert {n % 3 for n in range(10)} == {0, 1, 2}

# {} is a dict, set() is the empty set
assert isinstance({}, dict)
assert len(set()) == 0
