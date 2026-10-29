# Adapted from CPython's test suite (Lib/test/test_itertools.py: TestBasicOps.test_accumulate/test_chain/
# test_combinations/test_combinations_with_replacement/test_compress/test_count/test_cycle/test_groupby/
# test_filterfalse/test_zip_longest/test_product/test_repeat/test_islice/test_takewhile/test_dropwhile/
# test_tee/test_permutations/test_pairwise/test_batched/test_starmap).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every itertools function the dialect exposes is exercised here.

import itertools
from itertools import (count, repeat, cycle, islice, accumulate, chain, compress, dropwhile, takewhile,
                       filterfalse, groupby, starmap, zip_longest, tee, product, permutations,
                       combinations, combinations_with_replacement, pairwise, batched)

def take(n, it):
    return list(islice(it, n))

# ---- infinite iterators ----
assert take(4, count()) == [0, 1, 2, 3]
assert take(3, count(10)) == [10, 11, 12]
assert take(3, count(1, 3)) == [1, 4, 7]
assert take(3, count(0, -1)) == [0, -1, -2]
assert take(3, count(0.5, 0.5)) == [0.5, 1.0, 1.5]
assert take(2, count(step=2)) == [0, 2] and take(2, count(start=5)) == [5, 6]
assert take(3, count(2 ** 70)) == [2 ** 70, 2 ** 70 + 1, 2 ** 70 + 2]
assert repr(count(3)) == 'count(3)' and repr(count(1, 2)) == 'count(1, 2)'
try:
    count('a')
    assert False
except TypeError:
    pass
assert list(repeat('x', 3)) == ['x', 'x', 'x'] and list(repeat(1, 0)) == [] and list(repeat(1, -1)) == []
assert take(2, repeat(None)) == [None, None] and list(repeat(7, times=2)) == [7, 7]
assert list(map(pow, range(5), repeat(2))) == [0, 1, 4, 9, 16]
assert take(7, cycle('abc')) == ['a', 'b', 'c', 'a', 'b', 'c', 'a']
assert list(cycle([])) == [] and take(2, cycle(iter([1]))) == [1, 1]
try:
    cycle(5)
    assert False
except TypeError:
    pass

# ---- islice ----
assert list(islice('abcdefg', 2)) == ['a', 'b']
assert list(islice('abcdefg', 2, 4)) == ['c', 'd']
assert list(islice('abcdefg', 2, None)) == ['c', 'd', 'e', 'f', 'g']
assert list(islice('abcdefg', 0, None, 2)) == ['a', 'c', 'e', 'g']
assert list(islice('abcdefg', 1, 6, 3)) == ['b', 'e']
assert list(islice('abc', 0)) == [] and list(islice('abc', None)) == ['a', 'b', 'c'] and list(islice('abc', 10)) == ['a', 'b', 'c']
assert list(islice('abc', 5, 10)) == [] and list(islice('abcdef', None, None, 2)) == ['a', 'c', 'e']
assert list(islice(count(), 3, 7, 2)) == [3, 5]
it = iter(range(10))
assert list(islice(it, 3)) == [0, 1, 2] and next(it) == 3               # consumes exactly what it yields
for bad in (lambda: islice('abc', -1), lambda: islice('abc', 1, 2, 0), lambda: islice('abc', 'x'), lambda: islice('abc', 1, -1)):
    try:
        list(bad())
        assert False
    except ValueError:
        pass

# ---- accumulate ----
assert list(accumulate([1, 2, 3, 4])) == [1, 3, 6, 10]
assert list(accumulate([1, 2, 3, 4], lambda a, b: a * b)) == [1, 2, 6, 24]
assert list(accumulate([3, 1, 4, 1, 5], max)) == [3, 3, 4, 4, 5]
assert list(accumulate([])) == [] and list(accumulate([5])) == [5]
assert list(accumulate([1, 2, 3], initial=100)) == [100, 101, 103, 106]
assert list(accumulate([], initial=0)) == [0]
assert list(accumulate('abc')) == ['a', 'ab', 'abc'] and list(accumulate([[1], [2]])) == [[1], [1, 2]]
assert list(accumulate([1.5, 2.5])) == [1.5, 4.0]
assert list(accumulate(range(1, 5), lambda a, b: a - b, initial=10)) == [10, 9, 7, 4, 0]
assert list(accumulate([1, 2, 3], func=lambda a, b: a + b)) == [1, 3, 6]

# ---- chain ----
assert list(chain('ab', [1, 2], (3,))) == ['a', 'b', 1, 2, 3]
assert list(chain()) == [] and list(chain('a')) == ['a'] and list(chain([], [], [1])) == [1]
assert list(chain.from_iterable(['ab', 'cd'])) == ['a', 'b', 'c', 'd'] and list(chain.from_iterable([])) == []
assert list(chain.from_iterable([[1], (2, 3), range(4, 6)])) == [1, 2, 3, 4, 5]
assert sum(chain(range(3), range(3))) == 6 and take(2, chain(count(), 'x')) == [0, 1]
assert list(chain(*[[1, 2], [3]])) == [1, 2, 3]
try:
    list(chain(1, 2))
    assert False
except TypeError:
    pass

# ---- compress / dropwhile / takewhile / filterfalse ----
assert list(compress('ABCDEF', [1, 0, 1, 0, 1, 1])) == ['A', 'C', 'E', 'F']
assert list(compress('ABC', [True, False])) == ['A'] and list(compress('AB', [])) == [] and list(compress('', [1])) == []
assert list(compress(range(5), [0, '', None, [], 1])) == [4]
assert list(dropwhile(lambda x: x < 5, [1, 4, 6, 4, 1])) == [6, 4, 1]
assert list(dropwhile(lambda x: True, [1, 2])) == [] and list(dropwhile(lambda x: False, [1, 2])) == [1, 2]
assert list(dropwhile(lambda x: x, [])) == []
assert list(takewhile(lambda x: x < 5, [1, 4, 6, 4, 1])) == [1, 4]
assert list(takewhile(lambda x: False, [1])) == [] and list(takewhile(lambda x: True, 'ab')) == ['a', 'b']
assert list(takewhile(str.isdigit, '12a3')) == ['1', '2']
assert list(filterfalse(lambda x: x % 2, range(10))) == [0, 2, 4, 6, 8]
assert list(filterfalse(None, [0, 1, '', 'a', None, [1]])) == [0, '', None]
assert list(filterfalse(bool, [])) == []
assert list(filterfalse(str.isupper, 'aBcD')) == ['a', 'c']

# ---- groupby ----
assert [(k, list(g)) for k, g in groupby('aabbbcca')] == [('a', ['a', 'a']), ('b', ['b', 'b', 'b']), ('c', ['c', 'c']), ('a', ['a'])]
assert [k for k, g in groupby('aabbbcca')] == ['a', 'b', 'c', 'a']          # consecutive runs only
assert [(k, len(list(g))) for k, g in groupby([1, 1, 2, 3, 3, 3])] == [(1, 2), (2, 1), (3, 3)]
assert [(k, list(g)) for k, g in groupby(['apple', 'avocado', 'banana', 'cherry'], key=lambda s: s[0])] == \
    [('a', ['apple', 'avocado']), ('b', ['banana']), ('c', ['cherry'])]
assert [(k, list(g)) for k, g in groupby([1, 2, 3, 4], key=lambda v: v % 2)] == [(1, [1]), (0, [2]), (1, [3]), (0, [4])]
assert list(groupby([])) == []
assert [(k, list(g)) for k, g in groupby(sorted('abracadabra'))] == [('a', list('aaaaa')), ('b', ['b', 'b']), ('c', ['c']), ('d', ['d']), ('r', ['r', 'r'])]
assert {k: len(list(g)) for k, g in groupby(sorted('mississippi'))} == {'i': 4, 'm': 1, 'p': 2, 's': 4}
# the group iterator is shared with the parent: advancing the parent invalidates it
gb = groupby('aabb')
k1, g1 = next(gb)
k2, g2 = next(gb)
assert list(g1) == [] and list(g2) == ['b', 'b']
# run-length encoding idiom
assert [(k, len(list(g))) for k, g in groupby('aaabccdddd')] == [('a', 3), ('b', 1), ('c', 2), ('d', 4)]

# ---- starmap / zip_longest / tee ----
assert list(starmap(pow, [(2, 5), (3, 2), (10, 3)])) == [32, 9, 1000]
assert list(starmap(lambda a, b: a + b, [(1, 2), (3, 4)])) == [3, 7] and list(starmap(max, [[1, 5, 2]])) == [5]
assert list(starmap(lambda: 'x', [()])) == ['x'] and list(starmap(abs, [])) == []
assert list(zip_longest('ab', 'xyz')) == [('a', 'x'), ('b', 'y'), (None, 'z')]
assert list(zip_longest('ab', 'xyz', fillvalue='-')) == [('a', 'x'), ('b', 'y'), ('-', 'z')]
assert list(zip_longest('ab', [1, 2, 3], 'Q', fillvalue=0)) == [('a', 1, 'Q'), ('b', 2, 0), (0, 3, 0)]
assert list(zip_longest()) == [] and list(zip_longest('ab')) == [('a',), ('b',)] and list(zip_longest('', 'ab')) == [(None, 'a'), (None, 'b')]
assert list(zip_longest('ab', 'cd')) == list(zip('ab', 'cd'))
a, b = tee([1, 2, 3])
assert list(a) == [1, 2, 3] and list(b) == [1, 2, 3]
x, y, z = tee('ab', 3)
assert next(x) == 'a' and list(y) == ['a', 'b'] and list(x) == ['b'] and list(z) == ['a', 'b']
single = tee([1], 1)
assert len(single) == 1 and list(single[0]) == [1]
assert tee([], 0) == ()
src = iter([1, 2, 3])
t1, t2 = tee(src)
assert next(t1) == 1 and next(t1) == 2 and next(t2) == 1        # independent cursors over a shared buffer
try:
    tee([1], -1)
    assert False
except ValueError:
    pass

# ---- combinatorics ----
assert list(product('ab', [1, 2])) == [('a', 1), ('a', 2), ('b', 1), ('b', 2)]
assert list(product('ab', repeat=2)) == [('a', 'a'), ('a', 'b'), ('b', 'a'), ('b', 'b')]
assert list(product()) == [()] and list(product('ab', [])) == [] and list(product('a')) == [('a',)]
assert list(product([0, 1], repeat=3))[5] == (1, 0, 1) and len(list(product(range(3), repeat=3))) == 27
assert list(product('ab', repeat=0)) == [()]
assert list(product(range(2), 'x', [None])) == [(0, 'x', None), (1, 'x', None)]
assert list(permutations('abc')) == [('a', 'b', 'c'), ('a', 'c', 'b'), ('b', 'a', 'c'), ('b', 'c', 'a'), ('c', 'a', 'b'), ('c', 'b', 'a')]
assert list(permutations('abc', 2)) == [('a', 'b'), ('a', 'c'), ('b', 'a'), ('b', 'c'), ('c', 'a'), ('c', 'b')]
assert list(permutations(range(3), 0)) == [()] and list(permutations('ab', 3)) == [] and list(permutations([])) == [()]
assert list(permutations('aa')) == [('a', 'a'), ('a', 'a')]           # positions, not values
assert len(list(permutations(range(5)))) == 120 and len(list(permutations(range(5), 2))) == 20
assert list(combinations('abcd', 2)) == [('a', 'b'), ('a', 'c'), ('a', 'd'), ('b', 'c'), ('b', 'd'), ('c', 'd')]
assert list(combinations(range(4), 3)) == [(0, 1, 2), (0, 1, 3), (0, 2, 3), (1, 2, 3)]
assert list(combinations('abc', 0)) == [()] and list(combinations('abc', 4)) == [] and list(combinations('abc', 3)) == [('a', 'b', 'c')]
assert len(list(combinations(range(10), 3))) == 120
assert list(combinations_with_replacement('abc', 2)) == [('a', 'a'), ('a', 'b'), ('a', 'c'), ('b', 'b'), ('b', 'c'), ('c', 'c')]
assert list(combinations_with_replacement('ab', 3)) == [('a', 'a', 'a'), ('a', 'a', 'b'), ('a', 'b', 'b'), ('b', 'b', 'b')]
assert list(combinations_with_replacement('abc', 0)) == [()] and list(combinations_with_replacement('', 2)) == []
for bad in (lambda: combinations('abc', -1), lambda: permutations('abc', -1), lambda: combinations_with_replacement('a', -1)):
    try:
        list(bad())
        assert False
    except ValueError:
        pass
try:
    combinations('abc')
    assert False
except TypeError:
    pass
try:
    product('ab', repeat=-1)
    assert False
except ValueError:
    pass

# ---- pairwise / batched (3.10 / 3.12) ----
assert list(pairwise('abcd')) == [('a', 'b'), ('b', 'c'), ('c', 'd')]
assert list(pairwise('a')) == [] and list(pairwise('')) == [] and list(pairwise([1, 2])) == [(1, 2)]
assert list(pairwise(range(4))) == [(0, 1), (1, 2), (2, 3)]
assert [b - a for a, b in pairwise([1, 4, 9, 16])] == [3, 5, 7]
assert list(batched('abcdefg', 3)) == [('a', 'b', 'c'), ('d', 'e', 'f'), ('g',)]
assert list(batched('abcdef', 3)) == [('a', 'b', 'c'), ('d', 'e', 'f')] and list(batched('', 2)) == []
assert list(batched(range(5), 1)) == [(0,), (1,), (2,), (3,), (4,)] and list(batched('ab', 10)) == [('a', 'b')]
try:
    list(batched('abc', 0))
    assert False
except ValueError:
    pass
try:
    list(batched('abcd', 3, strict=True))
    assert False
except ValueError:
    pass
assert list(batched('abcd', 2, strict=True)) == [('a', 'b'), ('c', 'd')]

# ---- laziness and composition ----
seen = []
def spy(v):
    seen.append(v)
    return v
lazy = chain(map(spy, [1, 2, 3]), map(spy, [4]))
assert seen == []
assert next(lazy) == 1 and seen == [1]
assert list(islice(accumulate(count(1)), 5)) == [1, 3, 6, 10, 15]
assert take(5, (x for x in count() if x % 3 == 0)) == [0, 3, 6, 9, 12]
assert list(chain.from_iterable(product('ab', repeat=2)))[:4] == ['a', 'a', 'a', 'b']
assert [''.join(p) for p in permutations('abc', 2)][:3] == ['ab', 'ac', 'ba']
assert sum(1 for _ in combinations(range(6), 2)) == 15
assert dict(zip_longest('ab', [1])) == {'a': 1, 'b': None}
assert list(islice(cycle(pairwise('abc')), 4)) == [('a', 'b'), ('b', 'c'), ('a', 'b'), ('b', 'c')]
assert itertools.count is count and itertools.chain is chain
