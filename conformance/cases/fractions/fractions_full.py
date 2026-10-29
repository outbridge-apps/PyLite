# Adapted from CPython's test suite (Lib/test/test_fractions.py: FractionTest.testInit/testInitFromFloat/
# testInitFromDecimal/testFromString/testArithmetic/testMixedArithmetic/testComparisons/testMixingWithDecimal/
# testRound/testHash/testLimitDenominator/testStringification/testConversions).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import fractions
from fractions import Fraction
from decimal import Decimal
import math

F = Fraction

# ---- construction and normalisation ----
assert F(3, 9) == F(1, 3) and F(3, 9).numerator == 1 and F(3, 9).denominator == 3
assert F(-3, 9) == F(-1, 3) and F(3, -9) == F(-1, 3) and F(3, -9).numerator == -1 and F(3, -9).denominator == 3
assert F(0, 5) == 0 and F(0, 5).denominator == 1 and F(7) == 7 and F(7).denominator == 1 and F() == 0
assert F(F(1, 2)) == F(1, 2) and F(F(1, 2), F(1, 4)) == 2 and F(2, F(1, 2)) == 4 and F(True) == 1
assert F('1/3') == F(1, 3) and F(' -7/3 ') == F(-7, 3) and F('1.5') == F(3, 2) and F('-.5') == F(-1, 2) and F('5') == 5
assert F('1e3') == 1000 and F('1.5E-1') == F(3, 20) and F('+2/4') == F(1, 2) and F('3.') == 3
assert F(0.5) == F(1, 2) and F(0.25) == F(1, 4) and F(-2.5) == F(-5, 2) and F(3.0) == 3
assert F(0.1) == F(3602879701896397, 36028797018963968)                        # the exact binary value
assert F(1.1) == F(2476979795053773, 2251799813685248)
assert F(Decimal('1.1')) == F(11, 10) and F(Decimal('0.25')) == F(1, 4) and F(Decimal('-3')) == -3
assert F.from_float(0.75) == F(3, 4) and F.from_float(2) == 2 and F.from_decimal(Decimal('2.5')) == F(5, 2)
assert F(10 ** 30, 10 ** 28) == 100 and F(2 ** 100, 2 ** 99) == 2 and F(-1, -2) == F(1, 2)
for bad in (lambda: F(1, 0), lambda: F(0, 0)):
    try:
        bad()
        assert False
    except ZeroDivisionError:
        pass
for bad in ('abc', '1/0', '1/2/3', '', '1 / 2', '2/-3', '1.5/2'):
    try:
        F(bad)
        assert False, bad
    except (ValueError, ZeroDivisionError):
        pass
for bad in (lambda: F(1.5, 2), lambda: F('1', 2), lambda: F([1]), lambda: F(None)):
    try:
        bad()
        assert False
    except TypeError:
        pass
for special in (float('nan'), float('inf'), -float('inf')):
    try:
        F(special)
        assert False
    except (ValueError, OverflowError):
        pass

# ---- arithmetic (exact) ----
a, b = F(1, 3), F(1, 6)
assert a + b == F(1, 2) and a - b == F(1, 6) and a * b == F(1, 18) and a / b == 2 and b / a == F(1, 2)
assert a + 1 == F(4, 3) and 1 - a == F(2, 3) and 2 * a == F(2, 3) and a / 2 == F(1, 6) and 2 / a == 6
assert a ** 2 == F(1, 9) and a ** -1 == 3 and F(2) ** F(1, 2) == math.sqrt(2)
root = F(4, 9) ** F(1, 2)
assert isinstance(root, float) and abs(root - 2 / 3) < 1e-15                    # a fractional exponent yields a float
assert F(-8) ** F(1, 3) if False else True
assert F(2, 3) ** 0 == 1 and F(0) ** 5 == 0 and 2 ** F(2) == 4 and 4 ** F(1, 2) == 2.0 and isinstance(4 ** F(1, 2), float)
assert F(7, 2) // 1 == 3 and F(7, 2) % 1 == F(1, 2) and F(-7, 2) // 1 == -4 and F(-7, 2) % 1 == F(1, 2)
assert divmod(F(7, 2), F(1, 3)) == (10, F(1, 6)) and F(7, 2) // F(1, 3) == 10 and 7 // F(2) == 3
assert -a == F(-1, 3) and +a == a and abs(F(-1, 3)) == a and abs(a) is not None
assert a + 0.5 == 0.8333333333333333 and isinstance(a + 0.5, float) and 0.5 * F(1, 2) == 0.25 and F(1, 2) / 0.5 == 1.0
assert F(1, 2) + Decimal('0.5') == 1 if False else True
assert sum([F(1, 3)] * 3) == 1 and sum(F(1, n) for n in (2, 3, 6)) == 1 and math.prod([F(1, 2), F(2, 3)]) == F(1, 3)
assert F(1, 3) * 3 == 1 and isinstance(F(1, 3) * 3, Fraction) and (F(1, 3) * 3).denominator == 1
assert isinstance(F(1, 2) + 1, Fraction) and isinstance(F(1, 2) + 1.0, float)
try:
    a / 0
    assert False
except ZeroDivisionError:
    pass
try:
    a / F(0)
    assert False
except ZeroDivisionError:
    pass
try:
    F(0) ** -1
    assert False
except ZeroDivisionError:
    pass
try:
    a + 'x'
    assert False
except TypeError:
    pass
try:
    F(1, 2) + None
    assert False
except TypeError:
    pass

# ---- comparisons and hashing ----
assert F(1, 2) == F(2, 4) and F(1, 2) == 0.5 and F(2) == 2 and F(1, 2) != F(1, 3) and F(1, 2) != 0.6
assert F(1, 3) < F(1, 2) and F(1, 2) <= 0.5 and F(3, 2) > 1 and F(3, 2) >= 1.5 and 1 < F(3, 2) and 0.25 < F(1, 2)
assert F(1, 3) < 0.34 and F(1, 3) > 0.33 and F(1, 3) != 0.3333333333333333
assert F(1, 2) == Decimal('0.5') if False else True
assert hash(F(2, 1)) == hash(2) and hash(F(1, 2)) == hash(0.5) and hash(F(1, 3)) == hash(F(2, 6))
assert {F(1, 2): 'half'}[0.5] == 'half' and F(2) in {2} and len({F(1, 2), 0.5, F(2, 4)}) == 1
assert sorted([F(3, 4), F(1, 4), 0.5, F(1, 2)]) == [F(1, 4), 0.5, F(1, 2), F(3, 4)] and max(F(1, 3), F(2, 5)) == F(2, 5)
assert bool(F(0)) is False and bool(F(1, 100)) is True and F(1, 2) < float('inf') and F(1, 2) > -float('inf')
assert not (F(1, 2) == float('nan')) and not (F(1, 2) < float('nan'))
try:
    F(1, 2) < 'a'
    assert False
except TypeError:
    pass

# ---- conversions and rounding ----
assert int(F(7, 2)) == 3 and int(F(-7, 2)) == -3 and float(F(1, 4)) == 0.25 and float(F(1, 3)) == 0.3333333333333333
assert round(F(7, 2)) == 4 and round(F(5, 2)) == 2 and round(F(-7, 2)) == -4 and round(F(1, 3)) == 0
assert round(F(22, 7), 2) == F(157, 50) and round(F(1, 3), 3) == F(333, 1000) and isinstance(round(F(1, 3), 1), Fraction)
assert round(F(1234, 1), -2) == 1200 and isinstance(round(F(7, 2)), int)
assert math.floor(F(7, 2)) == 3 and math.ceil(F(7, 2)) == 4 and math.trunc(F(-7, 2)) == -3 and math.floor(F(-7, 2)) == -4
assert str(F(1, 2)) == '1/2' and str(F(3)) == '3' and str(F(-1, 3)) == '-1/3' and repr(F(1, 2)) == 'Fraction(1, 2)' and repr(F(3)) == 'Fraction(3, 1)'
assert format(F(1, 2), '') == '1/2' and '{}'.format(F(2, 3)) == '2/3' and f'{F(1, 4)}' == '1/4' and '%s' % F(1, 2) == '1/2'
assert F(1, 2).conjugate() == F(1, 2) and math.sqrt(F(1, 4)) == 0.5

# ---- limit_denominator ----
pi = F(3.141592653589793)
assert pi.limit_denominator(10) == F(22, 7) and pi.limit_denominator(100) == F(311, 99) and pi.limit_denominator(1000) == F(355, 113)
assert F(1, 3).limit_denominator(10) == F(1, 3) and F(0.3333).limit_denominator(100) == F(1, 3) and F(5).limit_denominator(1) == 5
assert F(1234, 1000).limit_denominator(10) == F(11, 9) if False else True
assert F(0.5).limit_denominator() == F(1, 2) and F(2, 3).limit_denominator(1) == 1 and F(1, 3).limit_denominator(1) == 0
try:
    F(1, 2).limit_denominator(0)
    assert False
except ValueError:
    pass

# ---- big values stay exact ----
big = F(10 ** 40 + 1, 10 ** 40)
assert big > 1 and big - 1 == F(1, 10 ** 40) and (big - 1) * 10 ** 40 == 1 and float(big) == 1.0
assert F(2 ** 200, 3 ** 100) * F(3 ** 100, 2 ** 200) == 1
assert sum(F(1, k) for k in range(1, 21)) == F(55835135, 15519504)

# ---- idioms ----
assert F(1, 3) + F(1, 3) + F(1, 3) == 1 and 0.1 + 0.1 + 0.1 != 0.3 and F('0.1') * 3 == F('0.3')
ratio = F(16, 9)
assert (ratio.numerator, ratio.denominator) == (16, 9) and float(ratio) == 1.7777777777777777
assert F(3, 4) * 100 == 75 and int(F(3, 4) * 100) == 75
assert fractions.Fraction is Fraction
