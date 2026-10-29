# Adapted from CPython's test suite (Lib/test/test_math.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import math

# trig at exact points
assert math.sin(0) == 0.0
assert math.cos(0) == 1.0
assert abs(math.sin(math.pi)) < 1e-12
assert abs(math.cos(math.pi) + 1) < 1e-12
assert abs(math.tan(math.pi / 4) - 1) < 1e-12
assert abs(math.atan2(1, 1) - math.pi / 4) < 1e-12

# degrees / radians round trip
assert math.degrees(math.pi) == 180.0
assert math.radians(180) == math.pi
assert abs(math.radians(math.degrees(1.234)) - 1.234) < 1e-12

# hypot and dist
assert math.hypot(3, 4) == 5.0
assert math.dist((0, 0), (3, 4)) == 5.0
assert math.hypot(0) == 0.0

# combinatorics
assert math.comb(5, 2) == 10
assert math.comb(5, 0) == 1
assert math.comb(5, 6) == 0
assert math.perm(5, 2) == 20
assert math.perm(5) == 120

# integer helpers
assert math.isqrt(16) == 4
assert math.isqrt(17) == 4
assert math.isqrt(0) == 0
assert math.lcm(4, 6) == 12
assert math.lcm(3, 5, 7) == 105
assert math.gcd(12, 18, 24) == 6

# prod and fsum
assert math.prod([1, 2, 3, 4]) == 24
assert math.prod([]) == 1
assert math.fsum([0.1] * 10) == 1.0          # fsum is exactly-rounded, sum() is not
assert sum([0.1] * 10) != 1.0

# exp / log family
assert math.exp(0) == 1.0
assert abs(math.log(math.exp(2)) - 2) < 1e-12
assert math.log(8, 2) == 3.0
assert abs(math.expm1(0)) == 0.0
assert math.log1p(0) == 0.0

# copysign / fmod / remainder / modf
assert math.copysign(3, -1) == -3.0
assert math.copysign(3, 1) == 3.0
assert math.fmod(7, 3) == 1.0
assert math.fmod(-7, 3) == -1.0              # fmod keeps the DIVIDEND's sign (unlike %)
assert -7 % 3 == 2
frac_part, int_part = math.modf(2.75)
assert int_part == 2.0 and frac_part == 0.75

# isclose tolerances
assert math.isclose(1.0, 1.0 + 1e-10)
assert not math.isclose(1.0, 1.1)
assert math.isclose(100, 101, rel_tol=0.02)
assert math.isclose(0.0, 1e-10, abs_tol=1e-9)

# tau
assert math.tau == 2 * math.pi
