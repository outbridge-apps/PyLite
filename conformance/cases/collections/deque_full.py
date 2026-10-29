# Adapted from CPython's test suite (Lib/test/test_deque.py: TestBasic.test_basics/test_maxlen/test_rotate/
# test_extendleft/test_remove/test_count/test_reverse/test_index/test_getitem/test_clear/test_repr).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import deque

# ---- construction and basics ----
d = deque()
assert len(d) == 0 and not d and list(d) == [] and d.maxlen is None
d = deque('abc')
assert list(d) == ['a', 'b', 'c'] and len(d) == 3 and bool(d)
assert deque([1, 2]) == deque([1, 2]) and deque([1, 2]) != deque([2, 1]) and deque() == deque()
assert deque(range(3)) == deque([0, 1, 2]) and deque(deque('xy')) == deque('xy')
assert 'b' in d and 'z' not in d
assert d[0] == 'a' and d[-1] == 'c' and d[1] == 'b'
try:
    d[3]
    assert False
except IndexError:
    pass
try:
    d[1:2]
    assert False
except TypeError:
    pass
d[0] = 'A'
assert list(d) == ['A', 'b', 'c']
del d[1]
assert list(d) == ['A', 'c']

# ---- append / appendleft / pop / popleft ----
d = deque()
d.append(1); d.append(2); d.appendleft(0)
assert list(d) == [0, 1, 2]
assert d.pop() == 2 and d.popleft() == 0 and list(d) == [1]
d.pop()
for fn in (d.pop, d.popleft):
    try:
        fn()
        assert False
    except IndexError:
        pass

# ---- extend / extendleft ----
d = deque([1])
d.extend([2, 3])
d.extendleft([0, -1])                    # reverses the order of the extension
assert list(d) == [-1, 0, 1, 2, 3]
d.extend('ab')
assert list(d)[-2:] == ['a', 'b']
d.extend(d)                              # extending with itself is safe (copy first)
assert len(d) == 14
e = deque()
e.extendleft(range(3))
assert list(e) == [2, 1, 0]

# ---- rotate ----
d = deque(range(5))
d.rotate(1)
assert list(d) == [4, 0, 1, 2, 3]
d.rotate(-1)
assert list(d) == [0, 1, 2, 3, 4]
d.rotate(2)
assert list(d) == [3, 4, 0, 1, 2]
d.rotate(-2)
d.rotate(7)                              # rotation wraps modulo len
assert list(d) == [3, 4, 0, 1, 2]
d.rotate(-7)
d.rotate()                               # default 1
assert list(d) == [4, 0, 1, 2, 3]
d.rotate(0)
assert list(d) == [4, 0, 1, 2, 3]
deque().rotate(3)                        # empty: no-op

# ---- remove / count / reverse / clear / index ----
d = deque('abcab')
d.remove('b')
assert list(d) == ['a', 'c', 'a', 'b']   # first occurrence only
try:
    d.remove('z')
    assert False
except ValueError:
    pass
assert d.count('a') == 2 and d.count('z') == 0
d.reverse()
assert list(d) == ['b', 'a', 'c', 'a']
d.clear()
assert len(d) == 0 and list(d) == []
d = deque('abcabc')
assert list(reversed(d)) == ['c', 'b', 'a', 'c', 'b', 'a']
assert sorted(d) == ['a', 'a', 'b', 'b', 'c', 'c'] and max(d) == 'c'

# ---- maxlen ----
d = deque(maxlen=3)
assert d.maxlen == 3
for i in range(5):
    d.append(i)
assert list(d) == [2, 3, 4]              # oldest items fall off the left
d.appendleft(9)
assert list(d) == [9, 2, 3]              # newest on the left pushes the right end off
d.extend([7, 8, 9, 10])
assert list(d) == [8, 9, 10]
d.extendleft([1, 2])
assert list(d) == [2, 1, 8]
assert deque('abcdef', maxlen=2) == deque('ef') and deque('abc', 3).maxlen == 3
assert deque(range(10), maxlen=0) == deque() and len(deque('abc', maxlen=0)) == 0
d0 = deque(maxlen=0)
d0.append(1)
assert len(d0) == 0
try:
    deque(maxlen=-1)
    assert False
except ValueError:
    pass
try:
    deque(maxlen='3')
    assert False
except TypeError:
    pass
full = deque([1, 2, 3], maxlen=3)
full.rotate(1)
assert list(full) == [3, 1, 2] and full.maxlen == 3

# ---- copy semantics and iteration ----
a = deque([1, [2]])
b = deque(a)
b[1].append(3)
assert a[1] == [2, 3]                    # shallow copy
assert [x for x in deque('ab')] == ['a', 'b'] and tuple(deque((1, 2))) == (1, 2)
it = iter(deque([1, 2]))
assert next(it) == 1
assert list(zip(deque('ab'), deque('cd'))) == [('a', 'c'), ('b', 'd')]

# ---- repr and comparison ----
assert repr(deque()) == 'deque([])' and repr(deque([1, 'a'])) == "deque([1, 'a'])"
assert repr(deque([1], maxlen=5)) == 'deque([1], maxlen=5)'
assert deque([1, 2]) < deque([1, 3]) and deque([1]) < deque([1, 0]) and deque([2]) > deque([1, 9])
assert deque([1, 2]) != [1, 2]           # a deque is not a list
# 3.5+ extras: copy / index / insert, + and * (a new deque keeps the LEFT operand's maxlen), += / *= in place
merged = deque([1, 2]) + deque([3])
assert merged == deque([1, 2, 3]) and deque([1], maxlen=2) + deque([2, 3]) == deque([2, 3]) and (deque([1], maxlen=2) + deque([2, 3])).maxlen == 2
assert deque([1, 2]) * 2 == deque([1, 2, 1, 2]) and deque([1, 2], maxlen=3) * 2 == deque([2, 1, 2]) and deque([1]) * 0 == deque()
try:
    deque([1]) + [2]
    assert False
except TypeError:
    pass
grow = deque([1])
alias = grow
grow += [2, 3]
grow *= 2
assert grow is alias and list(grow) == [1, 2, 3, 1, 2, 3]
cp = deque([1, 2, 3], maxlen=5).copy()
assert cp == deque([1, 2, 3]) and cp.maxlen == 5
assert deque([1, 2, 3]).index(2) == 1 and deque([1, 2, 3, 2]).index(2, 2) == 3 and deque([1, 2, 3]).index(1, -3, 1) == 0
try:
    deque([1]).index(9)
    assert False
except ValueError:
    pass
ins = deque([1, 2, 3])
ins.insert(1, 'a'); ins.insert(-1, 'b'); ins.insert(100, 'c'); ins.insert(-100, 'd')
assert list(ins) == ['d', 1, 'a', 2, 'b', 3, 'c']
try:
    deque([1, 2], maxlen=2).insert(0, 9)
    assert False
except IndexError:
    pass
try:
    hash(deque())
    assert False
except TypeError:
    pass

# ---- idioms: sliding window and bounded history ----
window = deque(maxlen=3)
sums = []
for v in [1, 2, 3, 4, 5]:
    window.append(v)
    sums.append(sum(window))
assert sums == [1, 3, 6, 9, 12]
def tail(items, n):
    return list(deque(items, maxlen=n))
assert tail(range(10), 3) == [7, 8, 9] and tail([], 3) == []
queue = deque()
queue.append('a'); queue.append('b')
assert queue.popleft() == 'a' and queue.popleft() == 'b'
stack = deque()
stack.append(1); stack.append(2)
assert stack.pop() == 2
