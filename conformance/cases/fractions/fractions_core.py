# Adapted from CPython's test suite (Lib/test/test_fractions.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from fractions import Fraction

# normalization to lowest terms
assert Fraction(2, 4) == Fraction(1, 2)
assert Fraction(-1, -2) == Fraction(1, 2)
assert Fraction(3, -6) == Fraction(-1, 2)
assert str(Fraction(1, 2)) == '1/2'
assert str(Fraction(4, 2)) == '2'

# exact arithmetic
assert Fraction(1, 3) + Fraction(1, 6) == Fraction(1, 2)
assert Fraction(1, 3) * 3 == 1
assert Fraction(1, 2) - Fraction(1, 3) == Fraction(1, 6)
assert Fraction(1, 2) / Fraction(1, 4) == 2
assert Fraction(2, 3) ** 2 == Fraction(4, 9)

# mixing with ints
assert Fraction(1, 2) + 1 == Fraction(3, 2)
assert 2 * Fraction(3, 4) == Fraction(3, 2)
assert Fraction(5) == 5

# comparisons
assert Fraction(1, 3) < Fraction(1, 2)
assert Fraction(1, 2) == Fraction(2, 4)
assert Fraction(1, 2) < 0.6
assert sorted([Fraction(3, 4), Fraction(1, 4), Fraction(1, 2)]) == [Fraction(1, 4), Fraction(1, 2), Fraction(3, 4)]

# numerator / denominator
half = Fraction(3, 6)
assert half.numerator == 1
assert half.denominator == 2

# string construction
assert Fraction('3/4') == Fraction(3, 4)
assert Fraction('0.5') == Fraction(1, 2)

# division by zero denominator
raised = False
try:
    Fraction(1, 0)
except ZeroDivisionError:
    raised = True
assert raised
