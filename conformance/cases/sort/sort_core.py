# Adapted from CPython's test suite (Lib/test/test_sort.py: TestBase.testStressfully, TestBugs,
# TestDecorateSortUndecorate, TestOptimizedCompares). Python Software Foundation, PSF License Version 2
# -- see conformance/README.md.
# Left out: the cases that need a subclass of int or list (WackyComparator, WackyList), __del__ and
# __class__ assignment, none of which the dialect has. Complains' class attribute is a module flag.
import random
from functools import cmp_to_key

random.seed(1)

def check(tag, expected, raw, compare=None):
    if compare:
        raw.sort(key=cmp_to_key(compare))
    else:
        raw.sort()
    assert len(expected) == len(raw), tag
    for i in range(len(expected)):
        assert expected[i] is raw[i], (tag, i)

def raises(exc, f):
    try:
        f()
    except exc:
        return True
    return False

# ================= TestBase.testStressfully =================
sizes = [0]
for power in range(1, 10):
    n = 2 ** power
    sizes.extend(range(n - 1, n + 2))
sizes.extend([10, 100, 1000])

maybe_complain = [True]

class Complains:
    def __init__(self, i):
        self.i = i
    def __lt__(self, other):
        if maybe_complain[0] and random.random() < 0.001:
            raise RuntimeError
        return self.i < other.i
    def __repr__(self):
        return "Complains(%d)" % self.i

class Stable:
    def __init__(self, key, i):
        self.key = key
        self.index = i
    def __lt__(self, other):
        return self.key < other.key
    def __repr__(self):
        return "Stable(%d, %d)" % (self.key, self.index)

for n in sizes:
    x = list(range(n))

    s = x[:]
    check("identity", x, s)

    s = x[:]
    s.reverse()
    check("reversed", x, s)

    s = x[:]
    random.shuffle(s)
    check("random permutation", x, s)

    y = x[:]
    y.reverse()
    s = x[:]
    check("reversed via function", y, s, lambda a, b: (b > a) - (b < a))

    # an insane comparison function must leave some permutation
    s = x[:]
    s.sort(key=cmp_to_key(lambda a, b: int(random.random() * 3) - 1))
    assert sorted(s) == x, "an insane function left some permutation"

    if len(x) >= 2:
        def bad_key(x):
            raise RuntimeError
        s = x[:]
        assert raises(RuntimeError, lambda: s.sort(key=bad_key))
        assert s == x   # a raising key leaves the list as it was

    x = [Complains(i) for i in x]
    s = x[:]
    random.shuffle(s)
    maybe_complain[0] = True
    it_complained = False
    try:
        s.sort()
    except RuntimeError:
        it_complained = True
    if it_complained:
        maybe_complain[0] = False
        check("exception during sort left some permutation", x, s)

    s = [Stable(random.randrange(10), i) for i in range(n)]
    augmented = [(e, e.index) for e in s]
    augmented.sort()    # forced stable because ties broken by index
    x = [e for e, i in augmented]   # a stable sort of s
    check("stability", x, s)

# ================= TestBugs =================
class C:
    def __lt__(self, other):
        if L and random.random() < 0.75:
            L.pop()
        else:
            L.append(3)
        return random.random() < 0.5

L = [C() for i in range(50)]
assert raises(ValueError, L.sort)

for i in range(20):
    def mutating_cmp(x, y):
        L.append(3)
        L.pop()
        return (x > y) - (x < y)
    L = [1, 2]
    assert raises(ValueError, lambda: L.sort(key=cmp_to_key(mutating_cmp)))
    def mutating_cmp(x, y):
        L.append(3)
        del L[:]
        return (x > y) - (x < y)
    assert raises(ValueError, lambda: L.sort(key=cmp_to_key(mutating_cmp)))

# ================= TestDecorateSortUndecorate =================
data = 'The quick Brown fox Jumped over The lazy Dog'.split()
copy = data[:]
random.shuffle(data)
data.sort(key=str.lower)
def my_cmp(x, y):
    xlower, ylower = x.lower(), y.lower()
    return (xlower > ylower) - (xlower < ylower)
copy.sort(key=cmp_to_key(my_cmp))
assert data == copy or [w.lower() for w in data] == [w.lower() for w in copy]

data = 'The quick Brown fox Jumped over The lazy Dog'.split()
assert raises(TypeError, lambda: data.sort(key=lambda x, y: 0))

data = [(random.randrange(100), i) for i in range(200)]
copy = data[:]
data.sort(key=lambda t: t[0])   # sort on the random first field
copy.sort()                     # sort using both fields
assert data == copy             # should get the same result

data = list(range(-2, 2))
dup = data[:]
assert raises(ZeroDivisionError, lambda: data.sort(key=lambda x: 1 / x))
assert data == dup

data = list(range(10))
def k(x):
    del data[:]
    data[:] = range(20)
    return x
assert raises(ValueError, lambda: data.sort(key=k))

data = list(range(100))
random.shuffle(data)
data.sort(reverse=True)
assert data == list(range(99, -1, -1))

data = [(random.randrange(100), i) for i in range(200)]
copy1 = data[:]
copy2 = data[:]
def my_cmp(x, y):
    x0, y0 = x[0], y[0]
    return (x0 > y0) - (x0 < y0)
def my_cmp_reversed(x, y):
    x0, y0 = x[0], y[0]
    return (y0 > x0) - (y0 < x0)
data.sort(key=cmp_to_key(my_cmp), reverse=True)
copy1.sort(key=cmp_to_key(my_cmp_reversed))
assert data == copy1
copy2.sort(key=lambda x: x[0], reverse=True)
assert data == copy2

# ================= TestOptimizedCompares =================
def check_against_rich_compare(L):
    random.seed(0)
    random.shuffle(L)
    L_1 = L[:]
    L_2 = [(x,) for x in L]
    L_3 = [((x,),) for x in L]
    for L in [L_1, L_2, L_3]:
        optimized = sorted(L)
        reference = [y[1] for y in sorted([(0, x) for x in L])]
        for (opt, ref) in zip(optimized, reference):
            assert opt is ref

for L in [[0, 'foo'], [0.0, 'foo'], [('foo',), 'foo']]:
    assert raises(TypeError, L.sort)
    assert raises(TypeError, [(x,) for x in reversed(L)].sort)
    assert raises(TypeError, [((x,),) for x in L].sort)

for L in [[1, 1.1], [1 << 70, 1.1], [1.1, 1], [1.1, 1 << 70]]:
    check_against_rich_compare(L)

# PointlessComparator (an __lt__ answering NotImplemented) is left out: the dialect has no NotImplemented.

for L in [list(range(100)) + [(1 << 70)],
          [str(x) for x in range(100)] + ['￿'],
          [bytes(x) for x in range(100)],
          [cmp_to_key(lambda x, y: x < y)(x) for x in range(100)]]:
    check_against_rich_compare(L)

check_against_rich_compare([str(x) for x in range(100)])
check_against_rich_compare([x for x in range(100)])
check_against_rich_compare([float(x) for x in range(100)])
check_against_rich_compare([float('nan')] * 100)
check_against_rich_compare([float('nan') for _ in range(100)])

assert raises(TypeError, [(1.0, 1.0), (False, "A"), 6].sort)
assert raises(TypeError, [('a', 1), (1, 'a')].sort)
assert raises(TypeError, [(1, 'a'), ('a', 1)].sort)

assert sorted([(None, 2), (None, 1)]) == [(None, 1), (None, 2)]
