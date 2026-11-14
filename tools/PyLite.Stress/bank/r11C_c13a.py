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
        print(fn, end="|")
        try:
            r = f(v)
            res.append(repr(r))
        except Exception as e:
            res.append(type(e).__name__[:4])
    print(fn, " ".join(res), flush=True)
TWO = ['atan2', 'copysign', 'fmod', 'hypot', 'pow', 'remainder', 'ldexp', 'nextafter', 'isclose', 'dist', 'gcd', 'lcm', 'comb', 'perm', 'log']
PAIRS = [(0.0, -0.0), (-0.0, -0.0), (INF, INF), (INF, -INF), (NAN, 1.0), (1.0, NAN), (1e308, 1e-308), (5e-324, -1.0), (2.0, 1024), (2.0, -1075), (-8.0, 1 / 3), (0.0, -1.0), (-0.0, -3.0), (1.0, INF), (-1.0, INF), (10, 3), (10 ** 30, 7), (-7, 3), (True, True), (2 ** 64, 2 ** 63), (5, -1), (0, 0), (1.5, 2), (1e300, 1e300)]
for fn in TWO:
    f = getattr(math, fn)
    res = []
    for a, b in PAIRS:
        print(fn, end="|")
        try:
            if fn == 'dist':
                r = f([a], [b])
            else:
                r = f(a, b)
            res.append(repr(r)[:30])
        except Exception as e:
            res.append(type(e).__name__[:4])
    print(fn, " ".join(res), flush=True)
