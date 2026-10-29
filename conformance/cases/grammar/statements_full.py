# Adapted from CPython's test suite (Lib/test/test_grammar.py: GrammarTests.test_del_stmt, test_global,
# test_nonlocal, test_augassign, test_assign, test_if/while/for, test_assert, test_import, test_pass,
# test_break_continue_loop, Lib/test/test_named_expressions.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- assignment forms ----
a = b = c = 7
assert a == 7 and b == 7 and c == 7
x, y = 1, 2
assert (x, y) == (1, 2)
x, y = y, x
assert (x, y) == (2, 1)
[p, q] = (3, 4)
assert p == 3 and q == 4
(m, (n, o)) = (1, (2, 3))
assert (m, n, o) == (1, 2, 3)
first, *rest = [1, 2, 3, 4]
assert first == 1 and rest == [2, 3, 4]
*init, last = 'abcd'
assert init == ['a', 'b', 'c'] and last == 'd'
head, *mid, tail = range(5)
assert head == 0 and mid == [1, 2, 3] and tail == 4
lone, = [9]
assert lone == 9
*only, = 'xy'
assert only == ['x', 'y']
lst = [0, 0, 0]
lst[0], lst[2] = 5, 6
assert lst == [5, 0, 6]
d = {}
d['k'], d['j'] = 1, 2
assert d == {'k': 1, 'j': 2}

try:
    aa, bb = [1, 2, 3]
    assert False
except ValueError as e:
    assert 'too many values to unpack' in str(e)
try:
    aa, bb, cc = [1, 2]
    assert False
except ValueError as e:
    assert 'not enough values to unpack' in str(e)

# ---- augmented assignment, all operators ----
v = 10
v += 5; assert v == 15
v -= 3; assert v == 12
v *= 2; assert v == 24
v //= 5; assert v == 4
v **= 3; assert v == 64
v %= 10; assert v == 4
v /= 8; assert v == 0.5
w = 0b1100
w &= 0b1010; assert w == 0b1000
w |= 0b0011; assert w == 0b1011
w ^= 0b1111; assert w == 0b0100
w <<= 2; assert w == 0b10000
w >>= 3; assert w == 0b10
s = 'ab'
s += 'cd'; assert s == 'abcd'
s *= 2; assert s == 'abcdabcd'
L = [1]
L += [2, 3]; assert L == [1, 2, 3]
L *= 2; assert L == [1, 2, 3, 1, 2, 3]
alias = L
alias += [0]
assert L[-1] == 0                     # list += mutates in place (alias sees it)
t = (1,)
t += (2,)
assert t == (1, 2)
dd = {'a': 1}
dd |= {'b': 2}
assert dd == {'a': 1, 'b': 2}
L2 = [10, 20, 30]
L2[1] += 5
assert L2 == [10, 25, 30]
dd['a'] += 100
assert dd['a'] == 101

# ---- del ----
n1 = 1
del n1
try:
    n1
    assert False
except NameError:
    pass
L3 = [0, 1, 2, 3, 4, 5]
del L3[0]
assert L3 == [1, 2, 3, 4, 5]
del L3[-1]
assert L3 == [1, 2, 3, 4]
del L3[1:3]
assert L3 == [1, 4]
L4 = list(range(10))
del L4[::2]
assert L4 == [1, 3, 5, 7, 9]
del L4[:]
assert L4 == []
d2 = {'a': 1, 'b': 2}
del d2['a']
assert d2 == {'b': 2}
try:
    del d2['zzz']
    assert False
except KeyError:
    pass
e1, e2 = 1, 2
del e1, e2
try:
    e2
    assert False
except NameError:
    pass

# ---- global / nonlocal ----
counter = 0
def bump():
    global counter
    counter += 1
bump(); bump()
assert counter == 2

def make_acc():
    total = 0
    def add(n):
        nonlocal total
        total += n
        return total
    return add
acc = make_acc()
acc(5)
assert acc(10) == 15
acc2 = make_acc()
assert acc2(1) == 1                     # independent cell per closure

def outer():
    val = 'outer'
    def inner():
        val = 'inner'               # a new local; no nonlocal
        return val
    inner()
    return val
assert outer() == 'outer'

# ---- if / elif / else ----
def sign(v):
    if v > 0:
        return 'pos'
    elif v < 0:
        return 'neg'
    else:
        return 'zero'
assert [sign(v) for v in (3, -3, 0)] == ['pos', 'neg', 'zero']
if 0:
    assert False
elif []:
    assert False
elif '':
    assert False
else:
    pass

# ---- while / for with else, break, continue ----
i = 0
while i < 3:
    i += 1
else:
    i = 100                       # else runs when the loop exits normally
assert i == 100
i = 0
while True:
    i += 1
    if i == 5:
        break
else:
    assert False                  # break skips else
assert i == 5
out = []
for k in range(10):
    if k % 2:
        continue
    if k > 6:
        break
    out.append(k)
else:
    assert False
assert out == [0, 2, 4, 6]
found = None
for k in range(3):
    pass
else:
    found = 'done'
assert found == 'done'
for k in []:
    assert False
for idx, (key, val) in enumerate([('a', 1), ('b', 2)]):
    assert (idx, key, val) in [(0, 'a', 1), (1, 'b', 2)]
# loop variable survives the loop
for last in range(4):
    pass
assert last == 3
# nested break only exits the inner loop
hits = 0
for _ in range(3):
    for _ in range(3):
        hits += 1
        break
assert hits == 3

# ---- pass / assert ----
def noop():
    pass
assert noop() is None
assert True, 'never shown'
try:
    assert 1 == 2, 'custom message'
    assert False
except AssertionError as e:
    assert str(e) == 'custom message'
try:
    assert []
except AssertionError as e:
    assert str(e) == ''

# ---- walrus ----
if (nlen := len('abc')) > 2:
    assert nlen == 3
data = [1, 2, 3, 4]
assert [y2 for v in data if (y2 := v * 2) > 4] == [6, 8]
assert (z := 10) == 10 and z == 10
while (chunk := data.pop()) > 2:
    pass
assert chunk == 2 and data == [1]

# ---- import forms ----
import math
import math as m
from math import sqrt
from math import floor as fl, ceil as ce
assert m.sqrt(16) == 4.0 and sqrt(9) == 3.0 and fl(2.5) == 2 and ce(2.5) == 3
import xml.etree.ElementTree as ET
assert ET.fromstring('<a/>').tag == 'a'
try:
    import no_such_module_xyz
    assert False
except ImportError as e:
    assert "No module named 'no_such_module_xyz'" in str(e)
try:
    from math import no_such_name
    assert False
except ImportError:
    pass

# ---- conditional expression ----
assert (1 if True else 2) == 1
assert (1 if False else 2) == 2
assert ('a' if 0 else 'b' if 1 else 'c') == 'b'
# only the taken branch is evaluated
def boom():
    raise RuntimeError('evaluated')
assert (1 if True else boom()) == 1

# ---- semicolons and line continuation ----
a1 = 1; b1 = 2; c1 = a1 + \
    b1
assert c1 == 3
total = (1 +
         2 +
         3)
assert total == 6

# ---- expression statements (values discarded) ----
1 + 1
'docstring-like string'
[1, 2, 3]
