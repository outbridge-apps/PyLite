from decimal import Decimal as D, getcontext, ROUND_HALF_UP, ROUND_DOWN, ROUND_CEILING, localcontext
from fractions import Fraction as F
def t(v, spec):
    try:
        print(spec, repr(format(v, spec)))
    except Exception as e:
        print(spec, 'EXC', type(e).__name__ + ':', e)
for v in [D('1E+2'), D('1.23456789'), D('100'), D('-0.5'), D('0'), D('0.00'), D('1234567.891'), D('-0'), D('99.97'), D('0.000001234'), D('1.5E-7'), D('2.665'), D('0E+3')]:
    for spec in ['f', '.2f', '.0f', 'e', '.2e', 'E', 'g', '.2', '.3g', 'G', 'n', '%', '.1%', ',.2f', '012,.2f', '>12.3e', '+.1f', ' g', '#.0f', '_g', '#g', '.0g', '010.2f', '^15,']:
        t(v, spec)
t(D(1), 'x')
t(D(1), 'd')
with localcontext() as c:
    c.rounding = ROUND_HALF_UP
    print(f'{D("2.665"):.2f}', format(D('2.5'), '.0f'), round(D('2.5')), round(D('3.5')), round(D('2.665'), 2))
    c.rounding = ROUND_DOWN
    print(format(D('1.999'), '.2f'), format(D('1.999'), '.2e'), format(D('-1.999'), '.2g'))
    c.rounding = ROUND_CEILING
    print(format(D('1.001'), '.2f'), format(D('-1.001'), '.2f'))
print(round(D('2.5')), round(D('-2.5')), round(D('2.675'), 2))
def p(fmt, v):
    try:
        print(repr(fmt % v))
    except Exception as e:
        print('EXC', type(e).__name__ + ':', e)
p('%.2f', D('1.005')); p('%d', D('7.9')); p('%d', D('-7.9')); p('%i', F(7, 2)); p('%.3f', F(1, 3)); p('%e', D('12.5'))
p('%x', D('7')); p('%d', 'x'); p('%f', 'x'); p('%g', F(-1, 8)); p('%5.1f', D('2.25')); p('%d', 7.9)
for v in [F(1, 3), F(-1, 3), F(2, 1), F(-7, 2), F(1234567, 3), F(0), F(1, 8), F(5, 2), F(-5, 2), F(1, 10**8), F(10**25, 3), F(999, 1000)]:
    for spec in ['', '.2f', 'f', '.0f', 'e', '.3e', 'g', '.3g', '%', '.1%', '>10', '<10', '^11', '+', '#', ',', '_.3f', '012.3f', '012,.3f', '=+12.2f', '0=12.2f', '*^14.4g', 'G', 'E', '.0g', '#.0f', '#g', 'x', '05', '<05.2f', '10']:
        t(v, spec)
