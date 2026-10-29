# Adapted from CPython's test suite (Lib/test/test_builtin.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# hash: equal values hash equal (within one run)
assert hash('abc') == hash('abc')
assert hash((1, 2)) == hash((1, 2))
assert hash(1) == hash(1)

# callable
assert callable(len)
assert callable(lambda: 1)
def named():
    return 1
assert callable(named)
assert not callable(42)
assert not callable('s')

# iter / next with and without a default
it = iter([1, 2])
assert next(it) == 1
assert next(it) == 2
assert next(it, 'done') == 'done'
raised = False
try:
    next(iter([]))
except StopIteration:
    raised = True
assert raised

# iter over a dict yields keys
assert list(iter({'a': 1, 'b': 2})) == ['a', 'b']

# zip strict raises on unequal lengths
assert list(zip([1, 2], 'ab', strict=True)) == [(1, 'a'), (2, 'b')]
raised = False
try:
    list(zip([1, 2, 3], 'ab', strict=True))
except ValueError:
    raised = True
assert raised

# min/max with default for empty iterables
assert max([], default=0) == 0
assert min([], default='none') == 'none'
assert max([5], default=0) == 5
raised = False
try:
    max([])
except ValueError:
    raised = True
assert raised

# type names
assert type(1).__name__ == 'int'
assert type('s').__name__ == 'str'
assert type([]).__name__ == 'list'
assert type(None).__name__ == 'NoneType'
assert type(1.5).__name__ == 'float'
assert type(True).__name__ == 'bool'

# issubclass
assert issubclass(bool, int)
assert not issubclass(int, bool)
assert issubclass(KeyError, LookupError)
assert issubclass(ZeroDivisionError, ArithmeticError)

# ascii escapes non-ASCII, repr keeps it
assert ascii('é') == "'\\xe9'"
assert ascii('abc') == "'abc'"

# sorted over mixed iterables
assert sorted({'b': 1, 'a': 2}) == ['a', 'b']              # keys
assert sorted({'b': 1, 'a': 2}.items()) == [('a', 2), ('b', 1)]
assert sorted('banana') == ['a', 'a', 'a', 'b', 'n', 'n']
assert sorted((3, 1, 2)) == [1, 2, 3]                       # always returns a list

# sum over generators; bool arithmetic
assert sum(v > 1 for v in [0, 2, 3]) == 2                   # counting via bools
assert sum([True, True, False]) == 2

# abs of bool/int/float
assert abs(True) == 1
assert abs(-0.0) == 0.0

# range equality and slicing-like behavior via list
assert list(range(3)) == [0, 1, 2]
assert range(3) == range(0, 3)
assert range(0) == range(5, 5)
