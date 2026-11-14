# allocmb: 64
# deadline: 20000
import fractions
l = [fractions.Fraction(i, 7) for i in range(1500000)]
print(len(l))
