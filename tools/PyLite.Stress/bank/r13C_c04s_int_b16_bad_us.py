# deadline: 20000
import math
from decimal import Decimal
from fractions import Fraction
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:100])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
inf = float('inf'); nan = float('nan')
class F:
    pass
class FB:
    pass
class FR:
    pass
class I:
    pass
class FS:
    pass
xs = [0.0, -0.0, 1.0, 2.0, 0.5, -0.5, -1.0, -2.0, -1.5, 1e-20, 1e-320, 5e-324, -5e-324, 171.0, 171.6, 171.7, -170.5,
      -171.5, -180.5, -190.5, -1e10 + 0.5, 1e308, -1e308, inf, -inf, nan, 2**53 + 0.0, -(2**53) + 0.0, 0.9999999999999999,
      1.0000000000000002, 2.0000000000000004, 1e-8, 3.5, 25.5, 100.25]
for fn in ('gamma', 'lgamma', 'erf', 'erfc'):
    f = getattr(math, fn)
    for x in xs:
        t(fn + '(' + repr(x) + ')', lambda f=f, x=x: f(x))
    for x in (True, False, 10**400, -10**400, 2**1023, Decimal('1.5'), Fraction(1, 3), F(), I(), FS(), 'a', None, [1], FB(), FR()):
        t(fn + '(' + type(x).__name__ + ')', lambda f=f, x=x: f(x))
    t(fn + '_kw', lambda f=f: f(x=1.0))
    t(fn + '_noarg', lambda f=f: f())
    t(fn + '_2arg', lambda f=f: f(1.0, 2.0))
# ints near limits and the str-digits limit
t('int_b16_bad_us', lambda: int('f__f', 16))
print(chr(10).join(out))
