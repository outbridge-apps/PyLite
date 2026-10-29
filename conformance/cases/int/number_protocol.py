# Adapted from CPython's test suite (Lib/test/test_int.py, test_builtin.py conversion cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# bin / hex / oct include their prefixes and handle negatives
assert bin(10) == '0b1010'
assert bin(-3) == '-0b11'
assert hex(255) == '0xff'
assert hex(-255) == '-0xff'
assert oct(8) == '0o10'
assert bin(0) == '0b0'

# format() builtin mirrors the format spec mini-language
assert format(255, 'x') == 'ff'
assert format(255, '#x') == '0xff'
assert format(10, 'b') == '1010'
assert format(3.14159, '.2f') == '3.14'
assert format(42, '>6') == '    42'
assert format(1234567, ',') == '1,234,567'
assert format('ab', '>4') == '  ab'

# three-argument pow (modular exponentiation)
assert pow(2, 10) == 1024
assert pow(2, 10, 1000) == 24
assert pow(3, 4, 5) == 1

# round on ints and with negative ndigits
assert round(7) == 7
assert round(12345, -2) == 12300
assert round(150, -2) == 200         # ties to even: 1.5 hundreds -> 2
assert round(250, -2) == 200         # 2.5 hundreds -> 2 (even)
assert round(350, -2) == 400         # 3.5 hundreds -> 4 (even)

# comparisons across int sizes
assert 2 ** 70 > 2 ** 60
assert -(2 ** 70) < 0
assert 2 ** 70 + 1 != 2 ** 70

# big // and % stay exact
big = 10 ** 40 + 7
assert big % 10 == 7
assert (big - 7) // 10 == 10 ** 39

# int <-> str round trip at scale
n = 98765432109876543210
assert int(str(n)) == n
assert len(str(10 ** 100)) == 101

# ---- the numeric tower: real/imag/numerator/denominator/conjugate ----
assert (5).real == 5 and (5).imag == 0 and (5).numerator == 5 and (5).denominator == 1
assert (-7).numerator == -7 and (-7).denominator == 1 and (5).conjugate() == 5
assert (1.5).real == 1.5 and (1.5).imag == 0.0 and (1.5).conjugate() == 1.5
assert '{0.real}/{0.imag}'.format(5) == '5/0'
assert '{0.numerator}/{0.denominator}'.format(7) == '7/1'
