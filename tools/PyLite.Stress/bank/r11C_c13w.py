# deadline: 30000
import math
def t(name, f):
    try:
        print(name + '=' + repr(f())[:150])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80])
INF = float('inf')
NAN = float('nan')
VALS = [0.0, -0.0, 1.0, -1.0, 0.5, 2.0, 1e-310, -1e-310, 5e-324, 1e308, -1e308, 1.7976931348623157e308, INF, -INF, NAN, 2 ** 53, 2 ** 64, -2 ** 63, 10 ** 400, -(10 ** 400), True, 171.0, 171.7, -171.5, -1e15 - 0.5, 1e22, 710.0, -745.2, 1e-20]
ONE = ['sqrt', 'exp', 'exp2', 'expm1', 'log', 'log2', 'log10', 'log1p', 'sin', 'cos', 'tan', 'asin', 'acos', 'atan', 'sinh', 'cosh', 'tanh', 'asinh', 'acosh', 'atanh', 'erf', 'erfc', 'gamma', 'lgamma', 'cbrt', 'fabs', 'floor', 'ceil', 'trunc', 'frexp', 'modf', 'degrees', 'radians', 'isfinite', 'isinf', 'isnan', 'ulp']
for fn in ONE:
    f = getattr(math, fn)
    res = []
    for v in VALS:
        try:
            r = f(v)
            res.append(repr(r))
        except Exception as e:
            res.append(type(e).__name__[:4])
    print(fn, ' '.join(res))
TWO = ['atan2', 'copysign', 'fmod', 'hypot', 'pow', 'remainder', 'ldexp', 'nextafter', 'isclose', 'dist', 'gcd', 'lcm', 'comb', 'perm', 'log']
PAIRS = [(0.0, -0.0), (-0.0, -0.0), (INF, INF), (INF, -INF), (NAN, 1.0), (1.0, NAN), (1e308, 1e-308), (5e-324, -1.0), (2.0, 1024), (2.0, -1075), (-8.0, 1 / 3), (0.0, -1.0), (-0.0, -3.0), (1.0, INF), (-1.0, INF), (10, 3), (10 ** 30, 7), (-7, 3), (True, True), (2 ** 64, 3), (5, -1), (0, 0), (1.5, 2), (1e300, 1e300)]
for fn in TWO:
    f = getattr(math, fn)
    res = []
    for a, b in PAIRS:
        try:
            if fn == 'dist':
                r = f([a], [b])
            else:
                r = f(a, b)
            res.append(repr(r)[:30])
        except Exception as e:
            res.append(type(e).__name__[:4])
    print(fn, ' '.join(res))
t('fma_inf0', lambda: math.fma(INF, 0.0, 1.0))
t('fma_over', lambda: math.fma(1e308, 10.0, 0.0))
t('fma_nan', lambda: math.fma(NAN, 1.0, 1.0))
t('fsum', lambda: [math.fsum([1e308, 1e308, -1e308]), math.fsum([0.1] * 10), math.fsum([INF, 1.0]), math.fsum([]), math.fsum([-0.0]), math.fsum([1e-308] * 10 ** 5)])
t('fsum_inf_nan', lambda: math.fsum([INF, -INF]))
t('fsum_over', lambda: math.fsum([1e308, 1e308]))
t('fsum_big_int', lambda: math.fsum([10 ** 400]))
t('prod', lambda: [math.prod([]), math.prod([2] * 1000) > 0, math.prod([1.5, 2], start=2), math.prod([[1]], start=1) if False else 0])
t('sumprod_len', lambda: math.sumprod([1], [1, 2]))
t('sumprod_mixed', lambda: math.sumprod([10 ** 30, 1.5], [3, 2]))
t('hypot_many', lambda: [math.hypot(), math.hypot(*([1e308] * 3)), math.hypot(3, 4, 12), math.hypot(INF, NAN), math.hypot(*range(10000)) > 0])
t('dist_bad', lambda: math.dist([1], [1, 2]))
t('factorial', lambda: [math.factorial(0), len(str(math.factorial(1000)))])
t('factorial_neg', lambda: math.factorial(-1))
t('factorial_float', lambda: math.factorial(5.0))
t('comb_huge_k', lambda: math.comb(2 ** 70, 2))
t('perm_huge', lambda: math.perm(2 ** 70, 3))
t('gcd_many', lambda: [math.gcd(), math.gcd(0, 0), math.gcd(*range(0, 10000, 6)), math.lcm(), math.lcm(*range(1, 100)).bit_length(), math.lcm(0, 5)])
t('log_big', lambda: [math.log(10 ** 400), math.log(10 ** 400, 10), math.log2(2 ** 10000), math.log10(10 ** 1000), math.log1p(10 ** 400) if False else 0])
t('log_base_bad', lambda: math.log(10, 1))
t('log_base0', lambda: math.log(10, 0))
t('log_zero_int', lambda: math.log(0))
t('log_neg_big', lambda: math.log(-(10 ** 400)))
t('sqrt_big', lambda: math.sqrt(10 ** 400))
t('ldexp_big', lambda: [math.ldexp(1.0, 2 ** 70), math.ldexp(1.0, -2 ** 70), math.ldexp(0.0, 2 ** 70)])
t('ldexp_over', lambda: math.ldexp(1.0, 2 ** 70))
t('ldexp_float', lambda: math.ldexp(1.0, 1.5))
t('nextafter_steps', lambda: [math.nextafter(0.0, 1.0, steps=2 ** 64), math.nextafter(1.0, 2.0, steps=0), math.nextafter(1.0, 0.0, steps=3), math.nextafter(INF, 0.0, steps=1)])
t('nextafter_neg', lambda: math.nextafter(1.0, 2.0, steps=-1))
t('isclose_bad', lambda: math.isclose(1.0, 1.0, rel_tol=-1))
t('isclose_kw', lambda: [math.isclose(1, 1 + 1e-10), math.isclose(INF, INF), math.isclose(NAN, NAN), math.isclose(0.0, 1e-300, abs_tol=1e-299)])
t('floor_big', lambda: [math.floor(1e300) % 10, math.ceil(-0.5), math.trunc(-1e20), math.floor(10 ** 400) == 10 ** 400])
t('floor_inf', lambda: math.floor(INF))
t('floor_nan', lambda: math.ceil(NAN))
t('round', lambda: [round(1e308, -308), round(5e-324, 323), round(5e-324, 324), round(0.5), round(1.5), round(-2.5), round(2.675, 2), round(1e22, 2), round(INF, 2), round(NAN, 1)])
t('round_inf_int', lambda: round(INF))
t('round_huge_nd', lambda: round(1.5, 10 ** 20))
t('divmod_float', lambda: [divmod(1e308, 1e-308) if False else divmod(7.5, -2), divmod(-0.0, 1.0), divmod(INF, 1.0), divmod(1.0, INF), divmod(-1.0, INF)])
t('divmod_zero', lambda: divmod(1.0, 0.0))
t('pow_mod_float', lambda: pow(2.0, 3, 5))
t('pow_huge_float', lambda: [2.0 ** -1100, (-2.0) ** 1023, 10.0 ** 308, 1.0000001 ** 1e10])
t('pow_over_float', lambda: 1.0000001 ** 1e15)
t('float_str', lambda: [str(x) for x in (1e16, 1.5e-5, 123456789012345680.0, 0.1 + 0.7, 1 / 3, 2 / 3 * 1e-310, 4.35, 9.999999999999999e22)])
t('float_format_more', lambda: ['%.17g' % 0.1, '%.0f' % 0.5, '%.0f' % 1.5, '%.0f' % 2.5, '%.1f' % 0.25, '%.1f' % 0.35, '%.3e' % 5e-324, '%a' % 1.0 if False else 0, '%.320f' % 5e-324][-1][-5:])
t('float_r', lambda: [float(repr(x)) == x for x in (5e-324, 1e-310, 2.2250738585072009e-308, 0.1, 1e23, 8.98846567431158e307, 1.7976931348623157e308)])
t('float_parse_halfway', lambda: [float('9007199254740993'), float('9007199254740995'), float('1.00000000000000011102230246251565404236316680908203125'), float('1.00000000000000011102230246251565404236316680908203124'), float('1.00000000000000011102230246251565404236316680908203126')])
t('float_parse_edges', lambda: [float('2.2250738585072011e-308'), float('2.2250738585072012e-308'), float('1.7976931348623158e308'), float('1.7976931348623159e308'), float('0.' + '0' * 400 + '1' + 'e400')])
t('float_parse_many_digits', lambda: float('0.' + '9' * 800))
t('float_parse_huge_exp', lambda: [float('1e' + '9' * 30), float('1e-' + '9' * 30), float('0e' + '9' * 30)])
t('float_parse_digits_exp', lambda: float('1' * 20000 + 'e-20000'))
t('float_parse_unicode', lambda: [float(chr(0x0661) + '.' + chr(0x0665)), float('  1.5 　')])
t('float_underscore', lambda: [float('1_0.0_1e1_0'), float('1.5e-1_0')])
t('float_underscore_bad', lambda: float('1_.5'))
t('float_hex_rt', lambda: [float.fromhex(x.hex()) == x for x in (5e-324, 0.1, 1e308, -0.0)])
t('int_float_cmp', lambda: [2 ** 1024 > INF, 2 ** 1024 > 1.7976931348623157e308, -(2 ** 1024) < -INF, 2 ** 53 + 1 > 9007199254740992.0, 10 ** 400 == INF, (2 ** 1024) == 1e308])
t('int_float_mixed_ops', lambda: [10 ** 400 // 1e300 if False else 0, (10 ** 30) % 7.5, 2 ** 1023 * 1.5])
t('int_float_over', lambda: 2 ** 1024 * 1.0)
t('int_truediv_exact', lambda: [(2 ** 1100 + 1) / 2 ** 1100, 10 ** 400 / 10 ** 399, (10 ** 400 + 1) / -(10 ** 399), 1 / 3, 2 ** 1074 / 2 ** 2148 if False else (1 / 2 ** 1074)])
t('int_truediv_under', lambda: 1 / 2 ** 1080)
