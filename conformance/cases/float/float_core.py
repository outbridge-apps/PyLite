# Adapted from CPython's test suite (Lib/test/test_float.py, round() cases from test_builtin.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# binary floating point is not decimal
assert 0.1 + 0.2 != 0.3
assert abs(0.1 + 0.2 - 0.3) < 1e-9
assert 0.5 + 0.25 == 0.75           # powers of two are exact

# round() is banker's rounding (half to even)
assert round(0.5) == 0
assert round(1.5) == 2
assert round(2.5) == 2
assert round(-0.5) == 0
assert round(-1.5) == -2
assert round(3.7) == 4
assert round(-3.7) == -4

# ndigits form; the classic binary-representation artifact
assert round(2.675, 2) == 2.67
assert round(3.14159, 2) == 3.14
assert round(1234.5678, -2) == 1200.0

# float arithmetic and floor division
assert 7.0 / 2 == 3.5
assert 7.0 // 2 == 3.0
assert -7.0 // 2 == -4.0
assert 7.5 % 2 == 1.5
assert 2.0 ** 10 == 1024.0

# conversions
assert float('1.5') == 1.5
assert float('  -2.25  ') == -2.25
assert float(7) == 7.0
assert float('1e3') == 1000.0
assert int(9.99) == 9

# mixed int/float comparisons
assert 1 == 1.0
assert 1.0 < 2
assert 10 ** 3 == 1000.0

# str() of simple floats
assert str(3.5) == '3.5'
assert str(1.0) == '1.0'
assert str(-0.25) == '-0.25'
