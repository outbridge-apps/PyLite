# Adapted from CPython's test suite (Lib/test/test_float.py; inf/nan cases from test_math.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import math

# infinity arithmetic and comparisons
inf = math.inf
assert inf > 10 ** 100
assert -inf < -(10 ** 100)
assert inf + 1 == inf
assert inf * 2 == inf
assert math.isinf(inf) and math.isinf(-inf)
assert not math.isinf(1e308)

# float('inf') parses
assert float('inf') == inf
assert float('-inf') == -inf
assert float('Infinity') == inf

# nan compares unequal to EVERYTHING, itself included
nan = math.nan
assert nan != nan
assert not (nan == nan)
assert not (nan < 1) and not (nan > 1)
assert math.isnan(nan)
assert math.isnan(float('nan'))
assert not math.isnan(0.0)

# isfinite
assert math.isfinite(1.5)
assert not math.isfinite(inf)
assert not math.isfinite(nan)

# division corner values
assert 0.1 * 3 != 0.3                  # accumulated representation error
assert 1e300 * 1e300 == inf            # overflow goes to inf
assert 1e-300 * 1e-300 == 0.0          # underflow goes to zero

# negative zero exists but equals positive zero
assert -0.0 == 0.0
assert math.copysign(1, -0.0) == -1.0
assert math.copysign(1, 0.0) == 1.0

# float is double precision: 2**53 boundary
assert 2.0 ** 53 == 2 ** 53
assert 2.0 ** 53 + 1 == 2.0 ** 53      # representation gap
assert float(2 ** 53 + 1) == float(2 ** 53)

# int(float) and round trips
assert int(1e10) == 10000000000
assert float(int(2.5)) == 2.0
