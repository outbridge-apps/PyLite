# Adapted from CPython's test suite (Lib/test/test_decimal.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from decimal import Decimal

# decimal arithmetic is exact where binary floats are not
assert Decimal('0.1') + Decimal('0.2') == Decimal('0.3')
assert 0.1 + 0.2 != 0.3
assert Decimal('1.10') + Decimal('2.20') == Decimal('3.30')
assert Decimal('0.1') * 3 == Decimal('0.3')

# significance is preserved in str()
assert str(Decimal('3.30')) == '3.30'
assert str(Decimal('1.10') + Decimal('2.20')) == '3.30'
assert str(Decimal('5')) == '5'

# value equality ignores trailing zeros
assert Decimal('1.0') == Decimal('1.00')
assert Decimal('1') == 1
assert Decimal('2.5') == 2.5

# comparisons and ordering
assert Decimal('0.1') < Decimal('0.2')
assert sorted([Decimal('3'), Decimal('1.5'), Decimal('2')]) == [Decimal('1.5'), Decimal('2'), Decimal('3')]

# arithmetic mixes with ints
assert Decimal('10') / 4 == Decimal('2.5')
assert Decimal('7') // 2 == Decimal('3')
assert Decimal('7') % 2 == Decimal('1')
assert Decimal('2') ** 10 == 1024
assert -Decimal('1.5') == Decimal('-1.5')
assert abs(Decimal('-2.5')) == Decimal('2.5')

# construction from int and float
assert Decimal(42) == Decimal('42')
assert Decimal('-0.5') < 0
