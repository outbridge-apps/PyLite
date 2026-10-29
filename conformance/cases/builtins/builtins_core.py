# Adapted from CPython's test suite (Lib/test/test_builtin.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# enumerate
assert list(enumerate('abc')) == [(0, 'a'), (1, 'b'), (2, 'c')]
assert list(enumerate('ab', start=1)) == [(1, 'a'), (2, 'b')]

# zip stops at the shortest input
assert list(zip([1, 2, 3], 'ab')) == [(1, 'a'), (2, 'b')]
assert list(zip()) == []
ks = ['a', 'b']
vs = [1, 2]
assert dict(zip(ks, vs)) == {'a': 1, 'b': 2}

# map / filter are lazy; list() drains them
assert list(map(lambda x: x * 2, [1, 2, 3])) == [2, 4, 6]
assert list(filter(lambda x: x > 1, [0, 1, 2, 3])) == [2, 3]
assert list(filter(None, [0, 1, '', 'a', [], [1]])) == [1, 'a', [1]]

# any / all with the empty-input identities and short-circuit
assert any([1, 0]) and not any([0, 0]) and not any([])
assert all([1, 1]) and not all([1, 0]) and all([])
seen = []
def look(x):
    seen.append(x)
    return x > 0
assert any(look(v) for v in [1, -1, -2])
assert seen == [1]                       # stopped after the first truthy

# sum with a start value
assert sum([1, 2, 3]) == 6
assert sum([1, 2], 10) == 13
assert sum([]) == 0
assert sum([0.5, 0.25]) == 0.75

# min / max over args and iterables, with key=
assert max(3, 1, 2) == 3
assert min([5, 2, 8]) == 2
assert max(['ab', 'c', 'abc'], key=len) == 'abc'
assert min(['ab', 'c', 'abc'], key=len) == 'c'

# sorted / reversed
assert sorted('cab') == ['a', 'b', 'c']
assert list(reversed([1, 2, 3])) == [3, 2, 1]
assert list(reversed('abc')) == ['c', 'b', 'a']

# range semantics
assert list(range(5)) == [0, 1, 2, 3, 4]
assert list(range(2, 10, 3)) == [2, 5, 8]
assert list(range(5, 0, -2)) == [5, 3, 1]
assert list(range(0)) == []
assert len(range(10)) == 10
assert 3 in range(10)
assert 10 not in range(10)

# truthiness
assert not bool('') and not bool([]) and not bool({}) and not bool(0) and not bool(None)
assert bool('0') and bool([0]) and bool(-1) and bool(' ')

# type conversions and reprs
assert str(None) == 'None'
assert str(True) == 'True'
assert str([1, 'a']) == "[1, 'a']"
assert repr('x') == "'x'"
assert repr([1, 'a']) == "[1, 'a']"

# isinstance with a tuple of types
assert isinstance(1, int)
assert isinstance(True, int)             # bool is an int subclass
assert isinstance('s', (int, str))
assert not isinstance('s', (int, float))

# len / abs / divmod / round on ints
assert len('abc') == 3 and len([1]) == 1 and len({'k': 1}) == 1
assert abs(-2.5) == 2.5
assert divmod(17, 5) == (3, 2)
assert round(7) == 7

# the script is the main module
assert __name__ == "__main__"
