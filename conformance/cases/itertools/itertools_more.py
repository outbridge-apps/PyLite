# Adapted from CPython's test suite (Lib/test/test_itertools.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import itertools

# accumulate with initial (3.8) and a custom function
assert list(itertools.accumulate([1, 2, 3], initial=100)) == [100, 101, 103, 106]
assert list(itertools.accumulate([], initial=5)) == [5]
assert list(itertools.accumulate([3, 1, 2], max)) == [3, 3, 3]
assert list(itertools.accumulate([3, 1, 2], min)) == [3, 1, 1]

# islice with None stop and only-a-stop
assert list(itertools.islice('abcdef', 2, None)) == ['c', 'd', 'e', 'f']
assert list(itertools.islice('abcdef', None)) == ['a', 'b', 'c', 'd', 'e', 'f']
assert list(itertools.islice('abcdef', 2, None, 2)) == ['c', 'e']

# chain of nothing / empties
assert list(itertools.chain()) == []
assert list(itertools.chain([], [], [1])) == [1]
assert list(itertools.chain.from_iterable([])) == []

# count with a negative step; float start
cnt = itertools.count(5, -2)
assert [next(cnt) for _ in range(4)] == [5, 3, 1, -1]

# product edge cases
assert list(itertools.product()) == [()]
assert list(itertools.product('ab')) == [('a',), ('b',)]
assert list(itertools.product('ab', '')) == []
assert len(list(itertools.product(range(3), repeat=3))) == 27

# permutations default r is the full length; ordering is lexicographic by position
perms = list(itertools.permutations('abc'))
assert len(perms) == 6
assert perms[0] == ('a', 'b', 'c')
assert perms[-1] == ('c', 'b', 'a')
assert list(itertools.permutations('ab', 5)) == []       # r > n -> empty

# combinations edge cases
assert list(itertools.combinations('abc', 0)) == [()]
assert list(itertools.combinations('abc', 3)) == [('a', 'b', 'c')]
assert list(itertools.combinations('abc', 4)) == []

# groupby with a key function; groups are consumed in order
data = ['apple', 'avocado', 'banana', 'blueberry', 'cherry']
summary = [(k, len(list(g))) for k, g in itertools.groupby(data, key=lambda w: w[0])]
assert summary == [('a', 2), ('b', 2), ('c', 1)]

# groupby on UNSORTED data creates repeated keys (the documented gotcha)
regrouped = [k for k, g in itertools.groupby('aabaa')]
assert regrouped == ['a', 'b', 'a']

# zip_longest default fill is None
assert list(itertools.zip_longest('ab', 'a')) == [('a', 'a'), ('b', None)]

# tee: advancing one branch does not advance the other
t1, t2 = itertools.tee(iter([1, 2, 3]))
assert next(t1) == 1
assert next(t1) == 2
assert next(t2) == 1

# starmap over tuples of differing arity via lambda
assert list(itertools.starmap(lambda a, b, c: a + b + c, [(1, 2, 3), (4, 5, 6)])) == [6, 15]

# repeat without a count is infinite; islice bounds it
assert list(itertools.islice(itertools.repeat('x'), 3)) == ['x', 'x', 'x']

# dropwhile/takewhile boundary: nothing dropped/taken
assert list(itertools.takewhile(lambda x: False, [1, 2])) == []
assert list(itertools.dropwhile(lambda x: False, [1, 2])) == [1, 2]
