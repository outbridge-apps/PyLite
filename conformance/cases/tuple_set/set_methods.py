# Adapted from CPython's test suite (Lib/test/test_set.py method cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# method forms accept any iterable (operators require sets)
s = {1, 2, 3}
assert s.union([3, 4]) == {1, 2, 3, 4}
assert s.intersection([2, 3, 9]) == {2, 3}
assert s.difference([1]) == {2, 3}
assert s.symmetric_difference([3, 4]) == {1, 2, 4}
assert s == {1, 2, 3}                     # none of the above mutated it

# in-place variants
t = {1, 2, 3}
t.update([4], [5])
assert t == {1, 2, 3, 4, 5}
t.intersection_update([1, 2, 3])
assert t == {1, 2, 3}
t.difference_update([3])
assert t == {1, 2}
t.symmetric_difference_update([2, 9])
assert t == {1, 9}

# predicates
assert {1, 2}.issubset({1, 2, 3})
assert {1, 2, 3}.issuperset({1, 2})
assert {1, 2}.isdisjoint({3, 4})
assert not {1, 2}.isdisjoint({2, 3})
assert set().issubset({1})
assert {1}.issubset({1})

# pop removes SOME element; clear empties
u = {42}
assert u.pop() == 42
assert u == set()
raised = False
try:
    set().pop()
except KeyError:
    raised = True
assert raised

# copy is independent at the top level
v = {1, 2}
w = v.copy()
w.add(3)
assert v == {1, 2}

# frozenset: immutable, hashable, usable as a dict key
fs = frozenset([1, 2])
assert fs == {1, 2}
assert len(fs) == 2
d = {fs: 'pair'}
assert d[frozenset([2, 1])] == 'pair'
raised = False
try:
    fs.add(3)
except AttributeError:
    raised = True
except TypeError:
    raised = True
assert raised

# sets of tuples (hashable elements)
edges = {(1, 2), (2, 3), (1, 2)}
assert len(edges) == 2
assert (1, 2) in edges

# comparison operators between sets are subset relations, not lexicographic
assert {1} < {1, 2}
assert not ({1, 4} < {1, 2, 3})
assert {1, 2} == {2, 1}

# a size change during iteration is an error; a same-size remove+add is not (CPython checks only the
# size) - the walk goes on in an unspecified order
s = {1, 2, 3}
try:
    for v in s:
        s.add(99)
    assert False
except RuntimeError as e:
    assert str(e) == 'Set changed size during iteration'
s = {1, 2, 3}
seen = 0
for v in s:
    seen += 1
    if seen == 1:
        s.discard(v)
        s.add(99)
assert seen >= 3 and len(s) == 3 and 99 in s       # no error; how many times the walk yields is unspecified
