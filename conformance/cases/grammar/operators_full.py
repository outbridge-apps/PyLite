# Adapted from CPython's test suite (Lib/test/test_grammar.py: test_binary_ops/test_comparison/
# test_unary_ops, Lib/test/test_bool.py, Lib/test/test_compare.py, Lib/test/test_long.py bit ops).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- arithmetic and precedence ----
assert 2 + 3 * 4 == 14
assert (2 + 3) * 4 == 20
assert 2 ** 3 ** 2 == 512                 # right-associative
assert -2 ** 2 == -4                      # unary minus binds looser than **
assert (-2) ** 2 == 4
assert 2 ** -1 == 0.5
assert 10 / 4 == 2.5 and 10 // 4 == 2 and 10 % 4 == 2
assert 7 // 2 == 3 and -7 // 2 == -4 and 7 // -2 == -4
assert 7 % 3 == 1 and -7 % 3 == 2 and 7 % -3 == -2 and -7 % -3 == -1
assert divmod(-7, 3) == (-3, 2)
assert 7.5 // 2 == 3.0 and 7.5 % 2 == 1.5 and -7.5 // 2 == -4.0
assert 1 / 3 * 3 == 1.0
assert 2 * 3 % 4 == 2
assert 10 - 2 - 3 == 5                    # left-associative
assert 100 / 10 / 2 == 5.0
assert 1 + 2 == 3 and 1.0 + 2 == 3.0
assert isinstance(1 + 2.0, float)
assert isinstance(4 / 2, float)
assert isinstance(4 // 2, int)
assert isinstance(2 ** 2, int) and isinstance(2 ** 0.5, float) and isinstance(2.0 ** 2, float)
assert 0 ** 0 == 1
assert 3 * 'ab' == 'ababab' and 'ab' * 0 == '' and 'ab' * -1 == ''
assert [1] * 3 == [1, 1, 1] and 3 * (1, 2) == (1, 2, 1, 2, 1, 2)
assert 'a' + 'b' == 'ab' and [1] + [2] == [1, 2] and (1,) + (2,) == (1, 2)

try:
    1 / 0
    assert False
except ZeroDivisionError:
    pass
try:
    1 // 0
    assert False
except ZeroDivisionError:
    pass
try:
    1 % 0
    assert False
except ZeroDivisionError:
    pass
try:
    1.0 / 0
    assert False
except ZeroDivisionError:
    pass
try:
    'a' + 1
    assert False
except TypeError:
    pass
try:
    [1] + (2,)
    assert False
except TypeError:
    pass
try:
    'a' - 'b'
    assert False
except TypeError:
    pass

# ---- unary ----
assert -(-5) == 5 and +5 == 5 and +(-5) == -5
assert not True is False and (not 0) is True
assert ~5 == -6 and ~-1 == 0 and ~0 == -1
assert -0.0 == 0.0
assert -True == -1 and +False == 0 and ~True == -2

# ---- bitwise ----
assert 0b1100 & 0b1010 == 0b1000
assert 0b1100 | 0b1010 == 0b1110
assert 0b1100 ^ 0b1010 == 0b0110
assert 1 << 10 == 1024 and 1024 >> 3 == 128
assert -8 >> 1 == -4 and -7 >> 1 == -4        # arithmetic shift
assert -1 & 0xFF == 255 and -256 | 15 == -241
assert 5 & -1 == 5 and -5 ^ -1 == 4
assert 1 << 100 == 2 ** 100
assert (1 << 100) >> 99 == 2
assert True & True == 1 and True | False == 1 and True ^ True == 0
assert isinstance(True & True, bool) and (True & True) is True
try:
    1 << -1
    assert False
except ValueError:
    pass
try:
    1.5 & 1
    assert False
except TypeError:
    pass

# ---- comparisons ----
assert 1 < 2 <= 2 == 2 != 3 > 0 >= 0
assert not (1 < 2 > 3)
assert 1 == 1.0 and 1 == True and 0 == False
assert 'a' < 'b' < 'ba' < 'bb'
assert 'Z' < 'a'
assert [1, 2] < [1, 3] and [1, 2] < [1, 2, 0] and [] < [0]
assert (1, 2) < (1, 3) and (1,) < (1, 0)
assert {1, 2} < {1, 2, 3} and not ({1, 2} < {1, 2})
assert {1, 2} <= {1, 2} and {1, 2, 3} > {1}
assert 1 != 'a' and [] != () and {} != []
# chained comparison evaluates the middle operand once
calls = []
def mid():
    calls.append(1)
    return 5
assert 1 < mid() < 10
assert len(calls) == 1

try:
    1 < 'a'
    assert False
except TypeError:
    pass
try:
    [1] < (1,)
    assert False
except TypeError:
    pass
try:
    None < 1
    assert False
except TypeError:
    pass

# ---- identity and membership ----
assert None is None and None is not 0
a = [1]; b = a; c = [1]
assert a is b and a is not c and a == c
assert 1 in [1, 2] and 3 not in [1, 2]
assert 'b' in 'abc' and 'bc' in 'abc' and '' in 'abc' and 'x' not in 'abc'
assert 1 in {1: 'a'} and 'a' not in {1: 'a'}
assert 1 in (1,) and 1 in {1} and 1 in range(3) and 3 not in range(3)
try:
    1 in 5
    assert False
except TypeError:
    pass
try:
    1 in 'abc'
    assert False
except TypeError:
    pass

# ---- boolean operators return operands, short-circuit ----
assert (0 or 'x') == 'x' and (1 or 'x') == 1
assert ([] or {} or 'z') == 'z'
assert (0 and 'x') == 0 and (1 and 'x') == 'x'
assert (1 and 2 and 3) == 3 and (1 and 0 and 3) == 0
assert (None or False) is False
def boom():
    raise RuntimeError('should not run')
assert (1 or boom()) == 1
assert (0 and boom()) == 0
assert not (1 and 0)
assert (not 1) is False
assert 1 and not 0
assert (1 < 2) + (2 < 3) == 2              # bools are ints

# ---- truthiness ----
for falsy in (0, 0.0, '', [], (), {}, set(), None, False, range(0), b''):
    assert not falsy
for truthy in (1, -1, 0.1, 'a', [0], (0,), {0: 0}, {0}, True, range(1), b'\x00'):
    assert truthy
assert bool([]) is False and bool([[]]) is True

# ---- container operators ----
assert {1, 2} | {3} == {1, 2, 3} and {1, 2} & {2, 3} == {2} and {1, 2} - {2} == {1} and {1, 2} ^ {2, 3} == {1, 3}
assert {'a': 1} | {'b': 2} == {'a': 1, 'b': 2}
assert ({'a': 1} | {'a': 2})['a'] == 2

# ---- slicing operator forms ----
s = 'abcdefgh'
assert s[2:5] == 'cde' and s[:3] == 'abc' and s[5:] == 'fgh' and s[:] == s
assert s[::2] == 'aceg' and s[::-1] == 'hgfedcba' and s[-3:] == 'fgh' and s[:-3] == 'abcde'
assert s[1:7:3] == 'be' and s[6:1:-2] == 'gec' and s[100:] == '' and s[-100:2] == 'ab'
assert s[5:2] == ''
L = list(range(10))
assert L[2:8:2] == [2, 4, 6] and L[::-3] == [9, 6, 3, 0] and L[-2:] == [8, 9]
assert (1, 2, 3, 4)[1:3] == (2, 3)

# ---- attribute / call / index chaining ----
assert 'a-b-c'.split('-')[1].upper() == 'B'
assert {'k': [1, 2, {'z': 'deep'}]}['k'][2]['z'] == 'deep'
assert (lambda: (lambda: 42))()() == 42

# ---- large-integer arithmetic keeps exactness ----
big = 10 ** 30
assert big + 1 - big == 1
assert (big * big) // big == big
assert big % 7 == (10 ** 30) % 7
assert 2 ** 64 - 1 == 18446744073709551615
assert -(2 ** 63) == -9223372036854775808
assert (2 ** 63) * 2 == 2 ** 64
assert 12345678901234567890 * 98765432109876543210 == 1219326311370217952237463801111263526900
assert (1 << 200) // (1 << 100) == 1 << 100
assert 10 ** 20 // 3 == 33333333333333333333
assert -10 ** 20 // 3 == -33333333333333333334
assert 10 ** 20 % 3 == 1 and -10 ** 20 % 3 == 2
assert divmod(10 ** 20, -3) == (-33333333333333333334, -2)
assert pow(2, 100, 1000) == 376
assert pow(3, -1, 7) == 5
assert 2 ** 100 / 2 ** 99 == 2.0
assert (2 ** 100) > (2 ** 99) and (2 ** 100) > 1.5
assert 10 ** 20 == 100000000000000000000 and 10 ** 20 == 1e20
