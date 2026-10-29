# Adapted from CPython's test suite (Lib/test/test_deque.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import deque

# both ends
d = deque([2, 3])
d.append(4)
d.appendleft(1)
assert list(d) == [1, 2, 3, 4]
assert d.pop() == 4
assert d.popleft() == 1
assert list(d) == [2, 3]

# extend on both ends; extendleft REVERSES its input
d = deque([3])
d.extend([4, 5])
d.extendleft([2, 1])                  # note: 2 first, then 1 in front of it
assert list(d) == [1, 2, 3, 4, 5]

# maxlen evicts from the far end
ring = deque(maxlen=3)
for v in range(5):
    ring.append(v)
assert list(ring) == [2, 3, 4]
ring.appendleft(9)
assert list(ring) == [9, 2, 3]        # eviction happened on the right

# rotate
r = deque([1, 2, 3, 4, 5])
r.rotate(2)
assert list(r) == [4, 5, 1, 2, 3]
r.rotate(-2)
assert list(r) == [1, 2, 3, 4, 5]

# len / in / iteration / indexing
q = deque('abc')
assert len(q) == 3
assert 'b' in q
assert q[0] == 'a' and q[-1] == 'c'
assert [ch for ch in q] == ['a', 'b', 'c']

# pop on empty raises IndexError
raised = False
try:
    deque().pop()
except IndexError:
    raised = True
assert raised

# count and clear
n = deque('aabab')
assert n.count('a') == 3
n.clear()
assert len(n) == 0
