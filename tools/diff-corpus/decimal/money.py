from decimal import Decimal, ROUND_HALF_UP, ROUND_HALF_EVEN, ROUND_DOWN, ROUND_CEILING, InvalidOperation, getcontext


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
cent = Decimal('0.01')
for v in ['2.675', '2.665', '-2.675', '0.005', '1.995']:
    d = Decimal(v)
    out.append([str(d.quantize(cent, rounding=r)) for r in (ROUND_HALF_UP, ROUND_HALF_EVEN, ROUND_DOWN, ROUND_CEILING)])
out.append(str(Decimal('19.99') * 3))
out.append(str(Decimal('100') / Decimal('3')))
out.append(str(Decimal('1.10') + Decimal('2.20')))
out.append(str(Decimal('1.30') - Decimal('1.3')))
out.append(Decimal('1.30') == Decimal('1.3'))
out.append(str(Decimal('2.50').normalize()))
out.append(str(Decimal('-0.00')))
out.append(repr(Decimal('3.14')))
out.append(str(Decimal(10) ** 3))
out.append(str(Decimal('7') // Decimal('2')))
out.append(str(Decimal('-7') // Decimal('2')))
out.append(str(Decimal('-7') % Decimal('2')))
out.append(Decimal('1.5').as_tuple())
out.append((int(Decimal('9.99')), float(Decimal('0.1')), round(Decimal('2.5')), round(Decimal('3.14159'), 2)))
out.append(str(sum([Decimal('0.1')] * 10)))
out.append(sorted([Decimal('2'), Decimal('1.5'), Decimal('10')]))
out.append(str(Decimal(0.5)))
out.append(getcontext().prec)
out.append(err(lambda: Decimal('abc')))
out.append(err(lambda: Decimal(1) / Decimal(0)))
out.append(issubclass(InvalidOperation, ArithmeticError))
result = out
