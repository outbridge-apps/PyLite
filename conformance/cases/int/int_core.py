# Adapted from CPython's test suite (Lib/test/test_int.py, test_grammar.py numeric parts).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# floor division and modulo track the divisor's sign
assert 7 // 2 == 3 and 7 % 2 == 1
assert -7 // 2 == -4 and -7 % 2 == 1
assert 7 // -2 == -4 and 7 % -2 == -1
assert -7 // -2 == 3 and -7 % -2 == -1
assert divmod(7, 2) == (3, 1)
assert divmod(-7, 2) == (-4, 1)

# the invariant behind it
for a in [7, -7, 13, -13]:
    for b in [2, -2, 5, -5]:
        assert (a // b) * b + (a % b) == a

# power
assert 2 ** 10 == 1024
assert 0 ** 0 == 1
assert (-2) ** 3 == -8
assert 2 ** -1 == 0.5
assert 10 ** 0 == 1

# arbitrary precision
assert 2 ** 100 == 1267650600228229401496703205376
big = 10 ** 30
assert big * big == 10 ** 60
assert (2 ** 64 - 1) + 1 == 2 ** 64
assert -(2 ** 63) - 1 == -9223372036854775809   # crosses the int64 boundary

# int() conversions
assert int('42') == 42
assert int('-17') == -17
assert int('  99  ') == 99          # surrounding whitespace is fine
assert int('1_000') == 1000         # underscores allowed in the string form too
assert int(3.99) == 3               # truncation toward zero
assert int(-3.99) == -3
assert int('ff', 16) == 255
assert int('101', 2) == 5
assert int('777', 8) == 511
assert int(True) == 1 and int(False) == 0

# numeric literals
assert 1_000_000 == 1000000
assert 0xff == 255 and 0o777 == 511 and 0b1010 == 10

# bool is an int
assert True == 1 and False == 0
assert True + True == 2
assert True * 10 == 10

# bit operations
assert 5 & 3 == 1
assert 5 | 3 == 7
assert 5 ^ 3 == 6
assert ~5 == -6
assert 1 << 10 == 1024
assert 1024 >> 3 == 128
assert -8 >> 1 == -4               # arithmetic (floor) shift

# comparisons and abs
assert abs(-42) == 42 and abs(42) == 42
assert max(3, -10, 7) == 7 and min(3, -10, 7) == -10
assert 1 == 1.0
assert 2 > 1.5
