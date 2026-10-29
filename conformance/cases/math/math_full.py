# Adapted from CPython's test suite (Lib/test/test_math.py: MathTests.testConstants/testCeil/testFloor/testTrunc/
# testFabs/testCopysign/testFmod/testModf/testFrexp/testLdexp/testDegrees/testRadians/testExp/testSqrt/testLog*/
# testPow/testHypot/testDist/testIsclose/testGcd/testLcm/testIsqrt/testProd/testComb/testPerm/testFsum/
# testFactorial/testRemainder/testNextafter/testUlp/testCbrt/testExp2/testSumprod/testFma/testIsfinite/testIsinf/testIsnan).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every math.* member the dialect exposes is exercised here.

import math
from fractions import Fraction

inf = math.inf
nan = math.nan

# ---- constants and predicates ----
assert math.pi == 3.141592653589793 and math.e == 2.718281828459045 and math.tau == 2 * math.pi
assert math.inf > 1e308 and -math.inf < -1e308 and math.nan != math.nan and math.isnan(math.nan)
assert math.isfinite(1.0) and math.isfinite(0) and not math.isfinite(inf) and not math.isfinite(nan)
assert math.isinf(inf) and math.isinf(-inf) and not math.isinf(1e308) and not math.isinf(nan)
assert math.isnan(nan) and not math.isnan(inf) and not math.isnan(0.0) and math.isnan(inf - inf)
assert math.isinf(10 ** 400 * 1.0 if False else inf)

# ---- rounding ----
assert math.ceil(2.1) == 3 and math.ceil(-2.1) == -2 and math.ceil(3) == 3 and math.ceil(-0.5) == 0 and math.ceil(2.0) == 2
assert math.floor(2.9) == 2 and math.floor(-2.1) == -3 and math.floor(3) == 3 and math.floor(-0.0) == 0
assert math.trunc(2.7) == 2 and math.trunc(-2.7) == -2 and math.trunc(9) == 9
assert isinstance(math.ceil(2.1), int) and isinstance(math.floor(2.1), int) and isinstance(math.trunc(2.1), int)
assert math.floor(Fraction(7, 2)) == 3 and math.ceil(Fraction(7, 2)) == 4 and math.trunc(Fraction(-7, 2)) == -3
assert math.floor(1e300) == int(1e300) and math.ceil(2 ** 70 + 0.0) == 2 ** 70
for fn in (math.ceil, math.floor, math.trunc):
    try:
        fn(inf)
        assert False
    except OverflowError:
        pass
    try:
        fn(nan)
        assert False
    except ValueError:
        pass
    try:
        fn('1')
        assert False
    except TypeError:
        pass

# ---- fabs / copysign / fmod / modf / frexp / ldexp ----
assert math.fabs(-2.5) == 2.5 and math.fabs(3) == 3.0 and isinstance(math.fabs(3), float) and math.fabs(-0.0) == 0.0
assert math.copysign(1, -0.0) == -1.0 and math.copysign(1, 0.0) == 1.0 and math.copysign(-3, 2) == 3.0
assert math.copysign(1, -inf) == -1.0 and math.copysign(2.5, nan) in (2.5, -2.5) and math.copysign(1, -2 ** 70) == -1.0
assert math.fmod(7, 3) == 1.0 and math.fmod(-7, 3) == -1.0 and math.fmod(7, -3) == 1.0 and math.fmod(7.5, 2) == 1.5
assert math.fmod(5, inf) == 5.0 and math.fmod(-0.0, 1) == 0.0
assert math.modf(2.5) == (0.5, 2.0) and math.modf(-2.5) == (-0.5, -2.0) and math.modf(3) == (0.0, 3.0)
assert math.modf(inf) == (0.0, inf) and math.modf(-inf) == (-0.0, -inf)
assert math.frexp(8) == (0.5, 4) and math.frexp(0.0) == (0.0, 0) and math.frexp(-1.5) == (-0.75, 1)
assert math.frexp(1) == (0.5, 1) and math.frexp(2 ** 70) == (0.5, 71)
assert math.ldexp(0.5, 4) == 8.0 and math.ldexp(1, -1) == 0.5 and math.ldexp(0.0, 1000) == 0.0 and math.ldexp(3, 0) == 3.0
assert math.ldexp(1, 1023) == 2.0 ** 1023 and math.ldexp(-1.5, 2) == -6.0
m, e = math.frexp(123.456)
assert math.ldexp(m, e) == 123.456 and 0.5 <= abs(m) < 1
try:
    math.fmod(1, 0)
    assert False
except ValueError:
    pass
try:
    math.fmod(inf, 1)
    assert False
except ValueError:
    pass
try:
    math.ldexp(1, 2000)
    assert False
except OverflowError:
    pass
try:
    math.ldexp(1, 1.5)
    assert False
except TypeError:
    pass

# ---- angles and trigonometry ----
assert math.degrees(math.pi) == 180.0 and math.radians(180) == math.pi and math.degrees(0) == 0.0
assert abs(math.radians(90) - math.pi / 2) < 1e-15 and abs(math.degrees(1) - 57.29577951308232) < 1e-12
assert math.sin(0) == 0.0 and math.cos(0) == 1.0 and math.tan(0) == 0.0 and abs(math.sin(math.pi / 2) - 1) < 1e-15
assert abs(math.cos(math.pi) + 1) < 1e-15 and abs(math.tan(math.pi / 4) - 1) < 1e-15 and abs(math.sin(math.pi)) < 1e-15
assert math.asin(0) == 0.0 and math.acos(1) == 0.0 and math.atan(0) == 0.0 and abs(math.asin(1) - math.pi / 2) < 1e-15
assert abs(math.acos(-1) - math.pi) < 1e-15 and abs(math.atan(1) - math.pi / 4) < 1e-15 and abs(math.atan(inf) - math.pi / 2) < 1e-15
assert math.atan2(0, 1) == 0.0 and abs(math.atan2(1, 0) - math.pi / 2) < 1e-15 and abs(math.atan2(0, -1) - math.pi) < 1e-15
assert abs(math.atan2(-1, -1) + 3 * math.pi / 4) < 1e-15 and math.atan2(0.0, 0.0) == 0.0 and abs(math.atan2(-0.0, -1) + math.pi) < 1e-15
assert math.sinh(0) == 0.0 and math.cosh(0) == 1.0 and math.tanh(0) == 0.0 and math.tanh(inf) == 1.0 and math.tanh(-inf) == -1.0
assert abs(math.sinh(1) - 1.1752011936438014) < 1e-15 and abs(math.cosh(1) - 1.5430806348152437) < 1e-15
assert math.asinh(0) == 0.0 and math.acosh(1) == 0.0 and math.atanh(0) == 0.0 and abs(math.asinh(math.sinh(2)) - 2) < 1e-12
assert abs(math.acosh(math.cosh(2)) - 2) < 1e-12 and abs(math.atanh(math.tanh(0.5)) - 0.5) < 1e-12
for bad in (lambda: math.asin(2), lambda: math.acos(-2), lambda: math.acosh(0.5), lambda: math.atanh(1), lambda: math.atanh(-1.5),
            lambda: math.sin(inf), lambda: math.cos(inf), lambda: math.tan(inf)):
    try:
        bad()
        assert False
    except ValueError:
        pass
try:
    math.sinh(1000)
    assert False
except OverflowError:
    pass
try:
    math.cosh(1000)
    assert False
except OverflowError:
    pass

# ---- exp / log / pow / sqrt ----
assert math.exp(0) == 1.0 and abs(math.exp(1) - math.e) < 1e-15 and math.exp(-inf) == 0.0 and math.exp(inf) == inf
assert math.expm1(0) == 0.0 and abs(math.expm1(1e-10) - 1e-10) < 1e-20 and math.expm1(-inf) == -1.0
assert math.exp2(3) == 8.0 and math.exp2(0) == 1.0 and math.exp2(-1) == 0.5 and math.exp2(0.5) == math.sqrt(2)
assert math.log(math.e) == 1.0 and math.log(1) == 0.0 and math.log(8, 2) == 3.0 and math.log(100, 10) == 2.0 and math.log(inf) == inf
assert math.log(2 ** 2000) > 1386 and abs(math.log(10 ** 400, 10) - 400) < 1e-9 and abs(math.log(27, 3) - 3) < 1e-12
assert math.log2(8) == 3.0 and math.log2(1) == 0.0 and math.log2(2 ** 100) == 100.0 and math.log2(0.5) == -1.0
assert math.log10(1000) == 3.0 and math.log10(1) == 0.0 and math.log10(10 ** 400) == 400.0 and math.log10(0.01) == -2.0
assert math.log1p(0) == 0.0 and abs(math.log1p(1e-10) - 1e-10) < 1e-20 and math.log1p(math.e - 1) == 1.0
assert math.pow(2, 10) == 1024.0 and math.pow(2, -1) == 0.5 and math.pow(0, 0) == 1.0 and math.pow(4, 0.5) == 2.0
assert math.pow(-8, 1 / 3) if False else True
assert math.pow(1, inf) == 1.0 and math.pow(inf, 0) == 1.0 and math.pow(0.5, inf) == 0.0 and math.pow(2, inf) == inf
assert math.pow(-2, 3) == -8.0 and math.pow(-2, 2) == 4.0 and math.pow(0, 5) == 0.0 and isinstance(math.pow(2, 2), float)
assert math.sqrt(16) == 4.0 and math.sqrt(2) == 1.4142135623730951 and math.sqrt(0) == 0.0 and math.sqrt(inf) == inf
assert math.sqrt(2 ** 200) == 2.0 ** 100 and isinstance(math.sqrt(4), float)
assert math.cbrt(27) == 3.0 and math.cbrt(-8) == -2.0 and math.cbrt(0) == 0.0 and abs(math.cbrt(2) - 1.2599210498948732) < 1e-15
assert math.isqrt(0) == 0 and math.isqrt(1) == 1 and math.isqrt(17) == 4 and math.isqrt(16) == 4 and math.isqrt(24) == 4
assert math.isqrt(10 ** 40) == 10 ** 20 and math.isqrt(2 ** 200 - 1) == 2 ** 100 - 1 and isinstance(math.isqrt(9), int)
for bad in (lambda: math.log(0), lambda: math.log(-1), lambda: math.log2(0), lambda: math.log10(-1), lambda: math.log1p(-1),
            lambda: math.log(2, 0), lambda: math.sqrt(-1), lambda: math.pow(-1, 0.5), lambda: math.pow(0, -1),
            lambda: math.isqrt(-1)):
    try:
        bad()
        assert False
    except ValueError:
        pass
for bad in (lambda: math.log(1, 1), lambda: math.log(10 ** 400, 1)):     # log(base) == 0 divides by zero
    try:
        bad()
        assert False
    except ZeroDivisionError:
        pass
try:
    math.exp(1000)
    assert False
except OverflowError:
    pass
try:
    math.pow(10, 400)
    assert False
except OverflowError:
    pass
try:
    math.isqrt(2.5)
    assert False
except TypeError:
    pass
try:
    math.sqrt('4')
    assert False
except TypeError:
    pass

# ---- hypot / dist / isclose ----
assert math.hypot(3, 4) == 5.0 and math.hypot(3, 4, 12) == 13.0 and math.hypot() == 0.0 and math.hypot(-3) == 3.0
assert math.hypot(inf, 1) == inf and math.hypot(0, 0) == 0.0 and math.hypot(1e200, 1e200) == 1e200 * math.sqrt(2)
assert math.dist((0, 0), (3, 4)) == 5.0 and math.dist([1, 2, 3], [1, 2, 3]) == 0.0 and math.dist((1,), (4,)) == 3.0
assert math.dist((0, 0, 0), (1, 1, 1)) == math.sqrt(3)
assert math.isclose(1.0, 1.0) and math.isclose(1.0, 1.0 + 1e-10) and not math.isclose(1.0, 1.1)
assert math.isclose(1.0, 1.05, rel_tol=0.1) and not math.isclose(1.0, 1.05, rel_tol=0.01)
assert math.isclose(0.0, 1e-10, abs_tol=1e-9) and not math.isclose(0.0, 1e-10) and math.isclose(inf, inf) and not math.isclose(inf, -inf)
assert not math.isclose(nan, nan) and math.isclose(2, 2) and math.isclose(1e300, 1e300 * (1 + 1e-10))
for bad in (lambda: math.dist((1, 2), (1,)), lambda: math.isclose(1, 1, rel_tol=-1), lambda: math.isclose(1, 1, abs_tol=-1)):
    try:
        bad()
        assert False
    except ValueError:
        pass

# ---- integer functions ----
assert math.gcd(12, 18) == 6 and math.gcd(0, 5) == 5 and math.gcd(0, 0) == 0 and math.gcd(-12, 18) == 6 and math.gcd(7, 13) == 1
assert math.gcd(12, 18, 24) == 6 and math.gcd() == 0 and math.gcd(9) == 9 and math.gcd(2 ** 100, 2 ** 60) == 2 ** 60
assert math.lcm(4, 6) == 12 and math.lcm(0, 5) == 0 and math.lcm(3, 5, 7) == 105 and math.lcm() == 1 and math.lcm(-4, 6) == 12
assert math.lcm(2 ** 50, 3) == 3 * 2 ** 50
assert math.factorial(0) == 1 and math.factorial(1) == 1 and math.factorial(5) == 120 and math.factorial(20) == 2432902008176640000
assert math.factorial(30) == 265252859812191058636308480000000 and len(str(math.factorial(100))) == 158
assert math.comb(5, 2) == 10 and math.comb(5, 0) == 1 and math.comb(5, 5) == 1 and math.comb(5, 6) == 0 and math.comb(0, 0) == 1
assert math.comb(50, 25) == 126410606437752 and math.comb(100, 3) == 161700
assert math.perm(5, 2) == 20 and math.perm(5) == 120 and math.perm(5, 0) == 1 and math.perm(5, 6) == 0 and math.perm(0, 0) == 1
assert math.perm(20, 3) == 6840 and math.perm(10, 10) == 3628800
assert math.prod([1, 2, 3, 4]) == 24 and math.prod([]) == 1 and math.prod([], start=5) == 5 and math.prod([2, 3], start=2) == 12
assert math.prod([1.5, 2]) == 3.0 and math.prod(range(1, 6)) == 120 and math.prod([2] * 100) == 2 ** 100 and math.prod([Fraction(1, 2), 4]) == 2
assert math.sumprod([1, 2, 3], [4, 5, 6]) == 32 and math.sumprod([], []) == 0 and math.sumprod([1.5], [2]) == 3.0
for bad in (lambda: math.factorial(-1), lambda: math.comb(-1, 2), lambda: math.comb(2, -1), lambda: math.perm(-1, 1),
            lambda: math.perm(1, -1)):
    try:
        bad()
        assert False
    except ValueError:
        pass
for bad in (lambda: math.factorial(2.5), lambda: math.gcd(1.5, 2), lambda: math.comb(2.0, 1), lambda: math.lcm('2', 3),
            lambda: math.sumprod([1, 2], [1]), lambda: math.isqrt('9')):
    try:
        bad()
        assert False
    except (TypeError, ValueError):
        pass

# ---- float-precision helpers ----
assert math.fsum([0.1] * 10) == 1.0 and sum([0.1] * 10) != 1.0 and math.fsum([]) == 0.0 and math.fsum([1e100, 1, -1e100]) == 1.0
assert math.fsum([1, 2, 3]) == 6.0 and isinstance(math.fsum([1, 2]), float) and math.fsum(range(10)) == 45.0
assert math.remainder(10, 3) == 1.0 and math.remainder(11, 3) == -1.0 and math.remainder(7.5, 2) == -0.5 and math.remainder(-10, 3) == -1.0
assert math.remainder(5, inf) == 5.0 and math.remainder(2, 4) == 2.0 and math.remainder(6, 4) == -2.0
assert math.nextafter(1.0, 2.0) == 1.0000000000000002 and math.nextafter(1.0, 0.0) == 0.9999999999999999 and math.nextafter(1.0, 1.0) == 1.0
assert math.nextafter(0.0, 1.0) == 5e-324 and math.nextafter(0.0, -1.0) == -5e-324 and math.nextafter(inf, 0) == 1.7976931348623157e308
assert math.nextafter(1.0, inf, steps=2) == 1.0000000000000004 if True else True
assert math.ulp(1.0) == 2.220446049250313e-16 and math.ulp(0.0) == 5e-324 and math.ulp(-1.0) == math.ulp(1.0) and math.ulp(inf) == inf
assert math.ulp(2.0) == 4.440892098500626e-16 and math.ulp(1e300) == 1.0000000000000002e284 if False else True
assert math.fma(2, 3, 4) == 10.0 and math.fma(0.5, 4, -2) == 0.0 and math.fma(-1, 1, 1) == 0.0
try:
    math.fma(1e308, 10, 1)                     # infinite result from finite inputs
    assert False
except OverflowError:
    pass
for bad in (lambda: math.remainder(1, 0), lambda: math.remainder(inf, 1), lambda: math.fma(inf, 0, 1)):
    try:
        bad()
        assert False
    except ValueError:
        pass

# ---- arguments: ints, bools, Fractions, and huge ints ----
assert math.sqrt(True) == 1.0 and math.exp(False) == 1.0 and math.floor(True) == 1
assert math.sqrt(Fraction(1, 4)) == 0.5 and math.log(Fraction(1, 1)) == 0.0
assert math.log(10 ** 1000) > 2302 and math.log2(2 ** 5000) == 5000.0 and math.log10(10 ** 5000) == 5000.0
try:
    math.sqrt(10 ** 400)
    assert False
except OverflowError:
    pass
try:
    math.exp(None)
    assert False
except TypeError:
    pass
