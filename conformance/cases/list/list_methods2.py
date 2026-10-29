# Adapted from CPython's test suite (Lib/test/test_list.py method cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# copy() is shallow
a = [1, [2]]
b = a.copy()
assert b == a and b is not a
b[0] = 99
assert a[0] == 1                 # top level independent
b[1].append(3)
assert a[1] == [2, 3]            # the inner list is shared

# clear()
c = [1, 2, 3]
c.clear()
assert c == []

# index with start (and the ValueError beyond it)
d = [1, 2, 1, 2]
assert d.index(2) == 1
assert d.index(2, 2) == 3
raised = False
try:
    d.index(3)
except ValueError:
    raised = True
assert raised

# remove raises when absent
raised = False
try:
    [1].remove(9)
except ValueError:
    raised = True
assert raised

# pop on empty raises IndexError
raised = False
try:
    [].pop()
except IndexError:
    raised = True
assert raised

# sort by composite key: primary desc via tuple trick
rows = [('b', 2), ('a', 2), ('c', 1)]
rows.sort(key=lambda r: (r[1], r[0]))
assert rows == [('c', 1), ('a', 2), ('b', 2)]
rows.sort(key=lambda r: (-r[1], r[0]))
assert rows == [('a', 2), ('b', 2), ('c', 1)]

# sorting mixed comparable values
assert sorted([3, 1.5, 2]) == [1.5, 2, 3]
assert sorted([True, False, 2]) == [False, True, 2]

# nested list equality is deep
assert [[1, [2]], 3] == [[1, [2]], 3]
assert [[1, [2]], 3] != [[1, [9]], 3]

# in on nested structures compares by equality
assert [1, 2] in [[1, 2], [3]]

# min/max with key over tuples
pts = [(1, 5), (2, 1), (3, 3)]
assert min(pts, key=lambda p: p[1]) == (2, 1)
assert max(pts, key=lambda p: p[1]) == (1, 5)

# list() copies, no aliasing
src = [1, 2]
dst = list(src)
dst.append(3)
assert src == [1, 2]

# repetition of an empty and negative count
assert [] * 5 == []
assert [1] * 0 == []
assert [1, 2] * -3 == []
