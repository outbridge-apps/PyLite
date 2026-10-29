# Adapted from CPython's test suite (Lib/test/test_iter.py, Lib/test/test_enumerate.py,
# Lib/test/test_range.py, Lib/test/test_sort.py, Lib/test/test_builtin.py: test_zip/test_map/test_filter/
# test_min/test_max/test_sum/test_any_all/test_reversed).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- iter / next ----
it = iter([1, 2, 3])
assert next(it) == 1 and next(it) == 2 and next(it) == 3
try:
    next(it)
    assert False
except StopIteration:
    pass
assert next(it, 'done') == 'done'
assert next(iter([]), None) is None
it2 = iter('ab')
assert list(it2) == ['a', 'b'] and list(it2) == []     # exhausted iterators stay exhausted
assert iter(it2) is it2                                # iterators are their own iterator
assert list(iter(range(3))) == [0, 1, 2]
assert list(iter({'a': 1, 'b': 2})) == ['a', 'b']
assert sorted(iter({3, 1, 2})) == [1, 2, 3]
assert list(iter((1, 2))) == [1, 2]
assert list(iter(b'ab')) == [97, 98]
# two-argument iter(callable, sentinel)
counter = [0]
def tick():
    counter[0] += 1
    return counter[0]
assert list(iter(tick, 4)) == [1, 2, 3]
try:
    iter(5)
    assert False
except TypeError:
    pass

# ---- enumerate ----
assert list(enumerate('ab')) == [(0, 'a'), (1, 'b')]
assert list(enumerate('ab', 1)) == [(1, 'a'), (2, 'b')]
assert list(enumerate('ab', start=-1)) == [(-1, 'a'), (0, 'b')]
assert list(enumerate([])) == []
assert [i for i, _ in enumerate(range(3))] == [0, 1, 2]
assert dict(enumerate('xy')) == {0: 'x', 1: 'y'}

# ---- zip ----
assert list(zip([1, 2, 3], 'ab')) == [(1, 'a'), (2, 'b')]        # shortest wins
assert list(zip()) == []
assert list(zip('a')) == [('a',)]
assert list(zip('ab', 'cd', 'ef')) == [('a', 'c', 'e'), ('b', 'd', 'f')]
assert list(zip([1, 2], [3, 4], strict=True)) == [(1, 3), (2, 4)]
try:
    list(zip([1, 2, 3], [1], strict=True))
    assert False
except ValueError:
    pass
pairs = [(1, 'a'), (2, 'b')]
nums, letters = zip(*pairs)                                       # unzip idiom
assert nums == (1, 2) and letters == ('a', 'b')
assert dict(zip('ab', [1, 2])) == {'a': 1, 'b': 2}
# zip is lazy
z = zip(iter([1, 2, 3]), iter('abc'))
assert next(z) == (1, 'a')

# ---- map / filter ----
assert list(map(abs, [-1, 2, -3])) == [1, 2, 3]
assert list(map(lambda a, b: a + b, [1, 2], [10, 20, 30])) == [11, 22]
assert list(map(str, [])) == []
assert list(map(int, '123')) == [1, 2, 3]
assert list(filter(None, [0, 1, '', 'a', None, [], [0]])) == [1, 'a', [0]]
assert list(filter(lambda v: v % 2, range(6))) == [1, 3, 5]
assert list(filter(str.isdigit, ['1', 'a', '22'])) == ['1', '22']
m = map(lambda v: v * 2, [1, 2])
assert next(m) == 2 and next(m) == 4
try:
    next(m)
    assert False
except StopIteration:
    pass

# ---- reversed ----
assert list(reversed([1, 2, 3])) == [3, 2, 1]
assert list(reversed('abc')) == ['c', 'b', 'a']
assert list(reversed((1, 2))) == [2, 1]
assert list(reversed(range(4))) == [3, 2, 1, 0]
assert list(reversed(range(0, 10, 3))) == [9, 6, 3, 0]
assert list(reversed([])) == []
assert ''.join(reversed('stressed')) == 'desserts'
try:
    reversed({1, 2})
    assert False
except TypeError:
    pass

# ---- range ----
r = range(10)
assert len(r) == 10 and r[0] == 0 and r[-1] == 9 and r[3] == 3
assert 5 in r and 10 not in r and -1 not in r and 5.0 in r
assert list(range(0)) == [] and list(range(-3)) == []
assert list(range(2, 5)) == [2, 3, 4]
assert list(range(0, 10, 3)) == [0, 3, 6, 9]
assert list(range(10, 0, -3)) == [10, 7, 4, 1]
assert list(range(5, 5)) == []
assert range(1, 10, 2)[2] == 5 and range(1, 10, 2)[-1] == 9
assert list(range(10)[2:5]) == [2, 3, 4]
assert list(range(10)[::-1]) == list(range(9, -1, -1))
assert list(range(10)[::3]) == [0, 3, 6, 9]
assert range(3) == range(0, 3) and range(0, 3, 1) == range(3)
assert range(0) == range(5, 5)                                    # both empty
assert len(range(0, 10, 4)) == 3 and len(range(10, 0, -4)) == 3
assert range(10).index(7) == 7 and range(10).count(7) == 1 and range(10).count(70) == 0
assert repr(range(3)) == 'range(0, 3)' and repr(range(1, 3, 2)) == 'range(1, 3, 2)'
assert r.start == 0 and r.stop == 10 and r.step == 1
assert range(10 ** 18)[-1] == 10 ** 18 - 1                        # no materialization
assert sum(range(101)) == 5050
assert max(range(5)) == 4 and min(range(5)) == 0
try:
    range(1, 2, 0)
    assert False
except ValueError:
    pass
try:
    range(1.5)
    assert False
except TypeError:
    pass
try:
    range(3)[3]
    assert False
except IndexError:
    pass

# ---- sorted ----
assert sorted([3, 1, 2]) == [1, 2, 3]
assert sorted([3, 1, 2], reverse=True) == [3, 2, 1]
assert sorted('cba') == ['a', 'b', 'c']
assert sorted({3: 'a', 1: 'b'}) == [1, 3]
assert sorted({'b', 'a'}) == ['a', 'b']
assert sorted(['bb', 'a', 'ccc'], key=len) == ['a', 'bb', 'ccc']
assert sorted(['bb', 'a', 'ccc'], key=len, reverse=True) == ['ccc', 'bb', 'a']
# stability
data = [('a', 2), ('b', 1), ('c', 2), ('d', 1)]
assert sorted(data, key=lambda t: t[1]) == [('b', 1), ('d', 1), ('a', 2), ('c', 2)]
assert sorted(data, key=lambda t: t[1], reverse=True) == [('a', 2), ('c', 2), ('b', 1), ('d', 1)]
assert sorted([(1, 'b'), (1, 'a'), (0, 'z')]) == [(0, 'z'), (1, 'a'), (1, 'b')]
assert sorted([2, 1.5, True, 0]) == [0, True, 1.5, 2]
assert sorted([-1, 3, -2], key=abs) == [-1, -2, 3]
assert sorted(['B', 'a', 'C'], key=str.lower) == ['a', 'B', 'C']
assert sorted([]) == []
orig = [3, 1]
assert sorted(orig) == [1, 3] and orig == [3, 1]                  # input untouched
try:
    sorted([1, 'a'])
    assert False
except TypeError:
    pass
try:
    sorted([1, 2], True)                                          # reverse is keyword-only
    assert False
except TypeError:
    pass

# ---- min / max ----
assert min(3, 1, 2) == 1 and max(3, 1, 2) == 3
assert min([3, 1, 2]) == 1 and max([3, 1, 2]) == 3
assert min('bca') == 'a' and max('bca') == 'c'
assert min([], default=0) == 0 and max([], default=None) is None
assert min(['bb', 'a', 'ccc'], key=len) == 'a' and max(['bb', 'a', 'ccc'], key=len) == 'ccc'
assert min([(1, 'z'), (1, 'a')]) == (1, 'a')
assert max([1, 1.5, True]) == 1.5
assert max(1, 2.0) == 2.0 and min(1, 1.0) == 1                    # first of equals
assert max([('a', 2), ('b', 2)], key=lambda t: t[1]) == ('a', 2)  # first maximal wins
assert min([('a', 2), ('b', 2)], key=lambda t: t[1]) == ('a', 2)
assert max({1: 'a', 2: 'b'}) == 2
try:
    min([])
    assert False
except ValueError:
    pass
try:
    max(1, 'a')
    assert False
except TypeError:
    pass
try:
    max()
    assert False
except TypeError:
    pass

# ---- sum / any / all ----
assert sum([1, 2, 3]) == 6 and sum([]) == 0 and sum([], 10) == 10
assert sum([1.5, 2.5]) == 4.0 and sum([1, 2.5]) == 3.5
assert sum([[1], [2]], []) == [1, 2]
assert sum(range(4), start=100) == 106
assert sum((True, True)) == 2
assert sum(x * x for x in range(4)) == 14
try:
    sum(['a', 'b'])
    assert False
except TypeError:
    pass
try:
    sum(['a', 'b'], '')                                            # str start is rejected
    assert False
except TypeError:
    pass
assert any([0, 0, 1]) and not any([0, 0]) and not any([])
assert all([1, 1]) and not all([1, 0]) and all([])
assert any(x > 2 for x in [1, 2, 3])
assert all(isinstance(x, int) for x in [1, 2])
# short-circuit: the generator is not fully consumed
seen = []
def probe(v):
    seen.append(v)
    return v
assert any(probe(v) for v in [0, 1, 2])
assert seen == [0, 1]
seen.clear()
assert not all(probe(v) for v in [1, 0, 2])
assert seen == [1, 0]

# ---- generator expressions are lazy single-pass iterators ----
g = (x * x for x in range(4))
assert next(g) == 0
assert list(g) == [1, 4, 9]
assert list(g) == []
assert sum(1 for _ in 'abc') == 3
assert list(zip((a for a in 'ab'), (b for b in 'xy'))) == [('a', 'x'), ('b', 'y')]
assert max((len(w), w) for w in ['aa', 'b', 'cc']) == (2, 'cc')
assert tuple(x for x in range(3)) == (0, 1, 2)
assert set(x % 2 for x in range(5)) == {0, 1}
assert dict((k, k * 2) for k in 'ab') == {'a': 'aa', 'b': 'bb'}

# ---- iteration order and mutation rules ----
d = {'b': 1, 'a': 2}
d['c'] = 3
assert list(d) == ['b', 'a', 'c']                                 # insertion order
assert list(d.items()) == [('b', 1), ('a', 2), ('c', 3)]
try:
    for k in d:
        d['z' + k] = 0
    assert False
except RuntimeError:
    pass
L = [1, 2, 3]
for x in list(L):                                                 # iterate a copy while mutating
    L.append(x)
assert L == [1, 2, 3, 1, 2, 3]

# ---- iteration over nested structures with unpacking ----
matrix = [[1, 2], [3, 4]]
assert [a + b for a, b in matrix] == [3, 7]
assert [x for row in matrix for x in row] == [1, 2, 3, 4]
assert [[row[i] for row in matrix] for i in range(2)] == [[1, 3], [2, 4]]  # transpose
assert list(zip(*matrix)) == [(1, 3), (2, 4)]
