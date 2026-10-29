# deadline: 3000
# allocmb: 1024
from fractions import Fraction
a = Fraction(3 ** 300000, 7 ** 170000)
b = Fraction(5 ** 200000, 11 ** 140000)
print(a + b > 0)
