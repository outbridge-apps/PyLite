# Adapted from CPython's test suite (Lib/test/test_math.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import math

assert math.floor(3.7) == 3
assert math.floor(-3.2) == -4
assert math.ceil(3.2) == 4
assert math.ceil(-3.7) == -3
assert math.floor(5) == 5 and math.ceil(5) == 5

assert math.sqrt(16) == 4.0
assert math.sqrt(2) ** 2 != 2          # binary float, again
assert abs(math.sqrt(2) ** 2 - 2) < 1e-9

assert math.fabs(-3.5) == 3.5
assert math.trunc(3.9) == 3
assert math.trunc(-3.9) == -3

assert 3.14159 < math.pi < 3.1416
assert 2.71828 < math.e < 2.71829

assert math.gcd(12, 18) == 6
assert math.gcd(7, 13) == 1
assert math.gcd(0, 5) == 5

assert math.factorial(0) == 1
assert math.factorial(5) == 120
assert math.factorial(20) == 2432902008176640000

assert math.pow(2, 10) == 1024.0        # always float, unlike **
assert math.log(math.e) == 1.0
assert abs(math.log10(1000) - 3.0) < 1e-12
assert math.log2(8) == 3.0

assert math.isclose(0.1 + 0.2, 0.3)
assert not math.isclose(0.1, 0.2)
