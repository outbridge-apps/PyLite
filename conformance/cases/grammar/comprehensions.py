# Adapted from CPython's test suite (Lib/test/test_listcomps.py, test_genexps.py, test_dictcomps.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# the loop variable does NOT leak out of a comprehension (py3 scoping)
i = 99
_ = [i for i in range(3)]
assert i == 99

# nested fors iterate in statement order
assert [(a, b) for a in range(2) for b in range(2)] == [(0, 0), (0, 1), (1, 0), (1, 1)]

# multiple if clauses AND together
assert [x for x in range(20) if x % 2 == 0 if x % 3 == 0] == [0, 6, 12, 18]

# if/else in the element position vs the filter position
assert [x if x > 0 else 0 for x in [-1, 2, -3]] == [0, 2, 0]

# inner loop may reference the outer variable
assert [y for x in [[1, 2], [3]] for y in x] == [1, 2, 3]
assert [x * y for x in range(1, 3) for y in range(1, x + 1)] == [1, 2, 4]

# set and dict comprehensions
assert {c for c in 'abca'} == {'a', 'b', 'c'}
assert {k: len(k) for k in ['a', 'bb']} == {'a': 1, 'bb': 2}
assert {n % 2: n for n in range(5)} == {0: 4, 1: 3}     # later keys overwrite

# generator expressions are lazy and single-pass
gen = (x * x for x in range(5))
assert next(gen) == 0
assert next(gen) == 1
assert list(gen) == [4, 9, 16]
assert list(gen) == []                                   # exhausted

# lazy evaluation is observable
log = []
def probe(x):
    log.append(x)
    return x
gen = (probe(x) for x in [1, 2, 3])
assert log == []                                         # nothing ran yet
assert next(gen) == 1
assert log == [1]

# genexp feeding aggregates without materializing
assert sum(x * 2 for x in range(4)) == 12
assert max(len(w) for w in ['a', 'abc', 'ab']) == 3
assert any(x > 2 for x in [1, 2, 3])
assert sorted(x % 3 for x in [5, 7, 9]) == [0, 1, 2]

# comprehension over dict items / enumerate
d = {'a': 1, 'b': 2}
assert [k + str(v) for k, v in d.items()] == ['a1', 'b2']
assert [i * c for i, c in enumerate('ab')] == ['', 'b']

# nested comprehension building a matrix, then transposing it
m = [[r * 3 + c for c in range(3)] for r in range(2)]
assert m == [[0, 1, 2], [3, 4, 5]]
assert [[row[c] for row in m] for c in range(3)] == [[0, 3], [1, 4], [2, 5]]

# conditional chains inside the element expression
labels = ['neg' if v < 0 else 'zero' if v == 0 else 'pos' for v in [-5, 0, 5]]
assert labels == ['neg', 'zero', 'pos']
