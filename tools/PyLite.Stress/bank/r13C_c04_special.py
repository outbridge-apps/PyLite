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
t('str4300', lambda: len(str(10**4299)))
t('str4301', lambda: len(str(10**4300)))
t('strneg4300', lambda: len(str(-10**4299)))
t('repr4301', lambda: len(repr(10**4300)))
t('fmt_d', lambda: len(format(10**4300, 'd')))
t('fmt_x', lambda: len(format(10**4300, 'x')))
t('fmt_comma', lambda: len(format(10**4299, ',')))
t('fmt_under', lambda: len(format(10**4299, '_d')))
t('fmt_n', lambda: len(format(10**4300, 'n')))
t('fmt_e', lambda: format(10**4300, 'e')[:20])
t('fmt_f', lambda: len(format(10**4300, 'f')))
t('fmt_g', lambda: format(10**4300, 'g'))
t('fmt_pct_int', lambda: len(format(10**4300, '%')))
t('pct_d', lambda: len('%d' % 10**4300))
t('pct_s', lambda: len('%s' % 10**4300))
t('pct_r', lambda: len('%r' % 10**4300))
t('pct_x', lambda: len('%x' % 10**4300))
t('pct_f', lambda: len('%f' % 10**4300))
t('pct_i_float', lambda: len('%d' % 1e300))
t('fstr', lambda: len(f'{10**4300}'))
t('fstr_eq', lambda: len(f'{10**4300=}'))
t('int_4300', lambda: int('9' * 4300) > 0)
t('int_4301', lambda: int('9' * 4301) > 0)
t('int_4301_us', lambda: int('9' * 2151 + '_' + '9' * 2150) > 0)
t('int_4301_sp', lambda: int(' ' * 100 + '9' * 4300 + ' ' * 100) > 0)
t('int_4301_lead0', lambda: int('0' * 10 + '9' * 4300) > 0)
t('int_b16_long', lambda: int('f' * 250000, 16).bit_length())
t('int_b2_long', lambda: int('1' * 1000000, 2).bit_length())
t('int_b8_long', lambda: int('7' * 333333, 8).bit_length())
t('int_b32_long', lambda: int('v' * 200000, 32).bit_length())
t('int_b36_4301', lambda: int('z' * 4301, 36) > 0)
t('int_b16_0x', lambda: int('0x' + 'f' * 100, 16).bit_length())
t('int_b0_long', lambda: int('0x' + 'f' * 100000, 0).bit_length())
t('int_b0_bin', lambda: int('0b' + '1_' * 1000 + '1', 0).bit_length())
t('int_b16_us', lambda: int('f_' * 100000 + 'f', 16).bit_length())
t('int_b16_bad_us', lambda: int('f__f', 16))
t('int_b16_trail_us', lambda: int('ff_', 16))
t('int_b16_unicode', lambda: int(chr(0xFF10) + 'f', 16))
t('int_b16_space_mid', lambda: int('f f', 16))
t('int_b16_neg', lambda: int('-' + 'f' * 1000, 16) < 0)
t('int_b16_plus', lambda: int('+' + 'f' * 1000, 16) > 0)
t('int_b16_empty', lambda: int('', 16))
t('int_b16_sign_only', lambda: int('-', 16))
t('int_b16_0x_only', lambda: int('0x', 16))
t('int_b4_long', lambda: int('3' * 500000, 4).bit_length())
t('int_from_bytes_huge', lambda: int.from_bytes(b'\xff' * 125000, 'big').bit_length())
t('hex_huge', lambda: len(hex(1 << 999999)))
t('str_bool', lambda: str(True) + format(True, 'd') + format(True, '.2f') + repr(True + 0))
t('str_int_sub', lambda: str(type('X', (int,), {})(10**4300)))
t('round_neg', lambda: round(10**4300, -4300) > 0)
t('decimal_from_big', lambda: len(str(Decimal(10**4300))))
t('float_big', lambda: float(10**308))
t('float_big2', lambda: float(10**309))
t('truediv_big', lambda: (10**4300) / (10**4299))
t('divmod_big', lambda: divmod(10**4300, 7)[1])
print(chr(10).join(out))
