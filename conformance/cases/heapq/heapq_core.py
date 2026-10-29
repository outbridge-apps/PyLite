# Adapted from CPython's test suite (Lib/test/test_heapq.py: TestHeap.test_push_pop / test_heapify /
# test_nsmallest / test_nlargest / test_heapreplace / test_heappushpop / test_merge). Python Software
# Foundation, PSF License Version 2 -- see README.
import heapq
from heapq import heappush, heappop, heapify, heapreplace, heappushpop, nlargest, nsmallest, merge

# push/pop yields the items in sorted order
data = [7, 2, 9, 4, 1, 8, 3, 6, 5, 0]
h = []
for x in data:
    heappush(h, x)
out = []
while h:
    out.append(heappop(h))
assert out == sorted(data)

# the heap invariant after heapify: every parent <= its children
h = list(data)
heapify(h)
for i in range(len(h)):
    for c in (2 * i + 1, 2 * i + 2):
        if c < len(h):
            assert h[i] <= h[c]
assert h[0] == 0

# heapreplace pops the smallest and pushes the new item; heappushpop is the cheaper push-then-pop
h = [1, 3, 5]
assert heapreplace(h, 4) == 1 and sorted(h) == [3, 4, 5]
assert heappushpop(h, 2) == 2 and sorted(h) == [3, 4, 5]     # smaller than the top: comes straight back
assert heappushpop(h, 9) == 3 and sorted(h) == [4, 5, 9]
try:
    heappop([])
    assert False
except IndexError:
    pass

# tuples order lexicographically, as everywhere
h = [(3, 'c'), (1, 'a'), (2, 'b')]
heapify(h)
assert [heappop(h) for _ in range(3)] == [(1, 'a'), (2, 'b'), (3, 'c')]

# nlargest / nsmallest == the sorted() slices, ties and all
data = [3, 1, 4, 1, 5, 9, 2, 6, 5, 3]
for n in range(0, 12):
    assert nlargest(n, data) == sorted(data, reverse=True)[:n]
    assert nsmallest(n, data) == sorted(data)[:n]
pairs = [(1, 'x'), (0, 'y'), (1, 'z')]
assert nlargest(2, pairs, key=lambda p: p[0]) == [(1, 'x'), (1, 'z')]
assert nsmallest(2, pairs, key=lambda p: p[0]) == [(0, 'y'), (1, 'x')]
assert nlargest(1, ['aa', 'b', 'ccc'], key=len) == ['ccc']

# merge is an iterator over the sorted union of sorted inputs
assert list(merge([1, 4, 7], [2, 5], [], [3, 6])) == [1, 2, 3, 4, 5, 6, 7]
assert list(merge([7, 4, 1], [5, 2], reverse=True)) == [7, 5, 4, 2, 1]
assert list(merge(['a', 'ccc'], ['bb'], key=len)) == ['a', 'bb', 'ccc']
it = merge([1], [2])
assert next(it) == 1 and next(it) == 2
try:
    next(it)
    assert False
except StopIteration:
    pass
