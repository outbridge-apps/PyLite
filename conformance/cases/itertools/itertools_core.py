# Adapted from CPython's test suite (Lib/test/test_itertools.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import itertools
from itertools import chain, islice, count, cycle, repeat

assert list(chain([1], [2, 3], 'ab')) == [1, 2, 3, 'a', 'b']
assert list(chain.from_iterable([[1, 2], [3]])) == [1, 2, 3]

assert list(islice('abcdef', 3)) == ['a', 'b', 'c']
assert list(islice('abcdef', 2, 4)) == ['c', 'd']
assert list(islice('abcdef', 0, 6, 2)) == ['a', 'c', 'e']
assert list(islice(count(10, 2), 3)) == [10, 12, 14]
assert list(islice(cycle('ab'), 5)) == ['a', 'b', 'a', 'b', 'a']
assert list(repeat(5, 3)) == [5, 5, 5]

assert list(itertools.product('ab', [1, 2])) == [('a', 1), ('a', 2), ('b', 1), ('b', 2)]
assert list(itertools.product(range(2), repeat=2)) == [(0, 0), (0, 1), (1, 0), (1, 1)]

perms = list(itertools.permutations('abc', 2))
assert len(perms) == 6
assert perms[0] == ('a', 'b')
combs = list(itertools.combinations('abcd', 2))
assert len(combs) == 6
assert combs[0] == ('a', 'b') and combs[-1] == ('c', 'd')
assert list(itertools.combinations_with_replacement('ab', 2)) == [('a', 'a'), ('a', 'b'), ('b', 'b')]

assert list(itertools.accumulate([1, 2, 3, 4])) == [1, 3, 6, 10]
assert list(itertools.accumulate([1, 2, 3], lambda a, b: a * b)) == [1, 2, 6]

grouped = [(k, list(g)) for k, g in itertools.groupby('aabbbc')]
assert grouped == [('a', ['a', 'a']), ('b', ['b', 'b', 'b']), ('c', ['c'])]

assert list(itertools.pairwise('abcd')) == [('a', 'b'), ('b', 'c'), ('c', 'd')]
assert list(itertools.batched('abcde', 2)) == [('a', 'b'), ('c', 'd'), ('e',)]

assert list(itertools.zip_longest('ab', 'a', fillvalue='-')) == [('a', 'a'), ('b', '-')]

assert list(itertools.takewhile(lambda x: x < 3, [1, 2, 3, 1])) == [1, 2]
assert list(itertools.dropwhile(lambda x: x < 3, [1, 2, 3, 1])) == [3, 1]
assert list(itertools.filterfalse(lambda x: x % 2, range(6))) == [0, 2, 4]
assert list(itertools.compress('abcd', [1, 0, 1, 0])) == ['a', 'c']
assert list(itertools.starmap(lambda a, b: a + b, [(1, 2), (10, 20)])) == [3, 30]

t1, t2 = itertools.tee([1, 2, 3])
assert list(t1) == [1, 2, 3] and list(t2) == [1, 2, 3]
