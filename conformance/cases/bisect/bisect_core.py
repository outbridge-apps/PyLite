# Adapted from CPython's test suite (Lib/test/test_bisect.py: TestBisect.test_precomputed_cases /
# test_negative_lo / test_large_range / test_keyword_args, TestInsort). Python Software Foundation,
# PSF License Version 2 -- see README.
import bisect
from bisect import bisect_left, bisect_right, insort, insort_left, insort_right

# ---- the precomputed table of Lib/test/test_bisect.py, right and left ----
for func, data, elem, expected in [
    (bisect_right, [], 1, 0),
    (bisect_right, [1], 0, 0), (bisect_right, [1], 1, 1), (bisect_right, [1], 2, 1),
    (bisect_right, [1, 1], 0, 0), (bisect_right, [1, 1], 1, 2), (bisect_right, [1, 1], 2, 2),
    (bisect_right, [1, 1, 1], 1, 3),
    (bisect_right, [1, 2], 0, 0), (bisect_right, [1, 2], 1, 1), (bisect_right, [1, 2], 1.5, 1),
    (bisect_right, [1, 2], 2, 2), (bisect_right, [1, 2], 3, 2),
    (bisect_right, [1, 1, 2, 2], 0, 0), (bisect_right, [1, 1, 2, 2], 1, 2), (bisect_right, [1, 1, 2, 2], 1.5, 2),
    (bisect_right, [1, 1, 2, 2], 2, 4), (bisect_right, [1, 1, 2, 2], 3, 4),
    (bisect_right, [1, 2, 3], 0, 0), (bisect_right, [1, 2, 3], 2, 2), (bisect_right, [1, 2, 3], 2.5, 2),
    (bisect_right, [1, 2, 3], 4, 3),
    (bisect_left, [], 1, 0),
    (bisect_left, [1], 0, 0), (bisect_left, [1], 1, 0), (bisect_left, [1], 2, 1),
    (bisect_left, [1, 1], 1, 0), (bisect_left, [1, 1], 2, 2),
    (bisect_left, [1, 2], 1, 0), (bisect_left, [1, 2], 1.5, 1), (bisect_left, [1, 2], 2, 1),
    (bisect_left, [1, 1, 2, 2], 1, 0), (bisect_left, [1, 1, 2, 2], 1.5, 2), (bisect_left, [1, 1, 2, 2], 2, 2),
    (bisect_left, [1, 2, 3], 2, 1), (bisect_left, [1, 2, 3], 2.5, 2), (bisect_left, [1, 2, 3], 4, 3),
]:
    assert func(data, elem) == expected, (func, data, elem)
    assert func(tuple(data), elem) == expected                  # a tuple searches too

assert bisect.bisect([1, 2, 2, 3], 2) == 3                       # bisect is bisect_right

# ---- lo / hi window ----
data = [1, 2, 3, 4, 5]
assert bisect_left(data, 3, 3) == 3 and bisect_right(data, 3, hi=2) == 2 and bisect_left(data, 9, hi=100) == 5
assert bisect_left(data, 3, lo=1, hi=4) == 2
try:
    bisect_left(data, 3, -1)
    assert False
except ValueError as e:
    assert str(e) == 'lo must be non-negative'

# ---- key= (3.10+): applied to the items, never to x, so x is already a key; insort keys x once ----
words = ['a', 'dd', 'ccc']
assert bisect_left(words, 2, key=len) == 1 and bisect_right(words, 2, key=len) == 2
pairs = [(1, 'a'), (3, 'c')]
insort(pairs, (2, 'b'), key=lambda p: p[0])
assert pairs == [(1, 'a'), (2, 'b'), (3, 'c')]

# ---- insort ----
a = [1, 3, 5]
insort(a, 4)
insort_left(a, 3)
insort_right(a, 3)
assert a == [1, 3, 3, 3, 4, 5]
b = []
for x in [5, 1, 4, 2, 3]:
    insort(b, x)
assert b == [1, 2, 3, 4, 5]
try:
    insort((1, 2), 3)
    assert False
except TypeError:
    pass
try:
    bisect_left([1, 'a'], 2)
    assert False
except TypeError:
    pass
