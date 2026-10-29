import math


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
out.append((math.floor(-2.5), math.ceil(-2.5), math.trunc(-2.5), round(-2.5), round(3.5), round(0.375, 2)))
out.append(math.fsum([0.1] * 10))
out.append(sum([0.1] * 10))
out.append((math.factorial(20), math.factorial(0)))
out.append((math.log(1), math.log10(100), math.log2(1024), math.log1p(1e-10), math.exp(1)))
out.append((math.hypot(3, 4), math.copysign(1, -0.0), math.fabs(-3)))
out.append((math.isfinite(1e308), math.isinf(float('inf')), math.isnan(float('nan'))))
out.append((math.frexp(8.0), math.ldexp(0.5, 4), math.modf(-3.75)))
out.append((math.degrees(math.pi), math.radians(180), math.sin(0), math.cos(math.pi), math.atan2(1, 1)))
out.append((math.pow(2, 10), math.sqrt(2), 2 ** 0.5))
out.append((math.fmod(-7, 3), -7 % 3, math.fmod(7.5, 2)))
out.append(err(lambda: math.sqrt(-1)))
out.append(err(lambda: math.log(0)))
out.append(err(lambda: math.factorial(-1)))
out.append(err(lambda: math.exp(1000)))
out.append(err(lambda: math.factorial(2.5)))
out.append(err(lambda: math.acos(2)))
result = out
