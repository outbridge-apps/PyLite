# Adapted from CPython's test suite (Lib/test/test_grammar.py and related).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# operator precedence
assert 2 + 3 * 4 == 14
assert (2 + 3) * 4 == 20
assert -5 ** 2 == -25          # unary minus binds looser than **
assert (-5) ** 2 == 25
assert 2 ** 3 ** 2 == 512      # ** is right-associative
assert 17 % 5 * 2 == 4
assert not True == False       # 'not' binds looser than '=='

# chained comparisons
assert 1 < 2 < 3
assert 3 > 2 > 1
assert 1 < 2 > 1.5
assert not (1 < 2 < 0)
assert 1 == 1.0 <= 2

# boolean operators return operands, short-circuit
assert (0 or 'x') == 'x'
assert (1 and 'y') == 'y'
assert ('' or []) == []
assert (None and 1) is None
log = []
def side(tag, val):
    log.append(tag)
    return val
_ = side('a', 0) or side('b', 1)
assert log == ['a', 'b']
log = []
_ = side('a', 1) or side('b', 1)
assert log == ['a']
log = []
_ = side('a', 0) and side('b', 1)
assert log == ['a']

# conditional expression
assert (1 if True else 2) == 1
assert (1 if [] else 2) == 2
assert ('y' if 0 else 'n' if 1 else 'x') == 'n'

# walrus
assert (n := 10) > 5
assert n == 10
vals = [y := 3, y ** 2]
assert vals == [3, 9]

# assignment forms
x = y = []
assert x is y
x.append(1)
assert y == [1]
a, b = 1, 2
assert (a, b) == (1, 2)
a, b = b, a
assert (a, b) == (2, 1)
first, *rest = [1, 2, 3, 4]
assert first == 1 and rest == [2, 3, 4]
*init, last = 'abc'
assert init == ['a', 'b'] and last == 'c'
(p, (q, r)) = (1, (2, 3))
assert p + q + r == 6

# augmented assignment
i = 5
i += 2
i *= 3
i -= 1
i //= 4
assert i == 5
s = 'ab'
s += 'cd'
assert s == 'abcd'
lst = [1]
lst += [2]
lst *= 2
assert lst == [1, 2, 1, 2]
d = [10, 20]
d[1] += 5
assert d == [10, 25]

# identity and membership
assert None is None
none_var = None
assert none_var is None
assert not (none_var is not None)
assert 2 in [1, 2, 3]
assert 'b' in 'abc'
assert 'x' not in 'abc'
assert 'k' in {'k': 1}

# del
tmp = {'a': 1, 'b': 2}
del tmp['a']
assert tmp == {'b': 2}
