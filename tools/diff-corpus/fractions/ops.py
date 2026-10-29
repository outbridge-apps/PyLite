from fractions import Fraction


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
a = Fraction(3, 4)
b = Fraction('5/6')
out.append([str(x) for x in (a + b, a - b, a * b, a / b, a ** 2, -a, abs(-a))])
out.append(repr(Fraction(6, -8)))
out.append(str(Fraction('1.25')))
out.append(str(Fraction(0.75)))
out.append(str(Fraction(1, 3).limit_denominator(10)))
out.append(str(Fraction(3.141592653589793).limit_denominator(1000)))
out.append((a.numerator, a.denominator, float(a), int(Fraction(7, 2)), round(Fraction(7, 2))))
out.append(Fraction(1, 2) == 0.5)
out.append(Fraction(1, 3) < 0.34)
out.append(sorted([Fraction(1, 3), Fraction(1, 4), Fraction(2, 5)]))
out.append(str(Fraction(5, 3) // 1))
out.append(str(Fraction(-5, 3) % 1))
out.append(hash(Fraction(1, 2)) == hash(0.5))
out.append(err(lambda: Fraction(1, 0)))
out.append(err(lambda: Fraction('x/2')))
result = out
