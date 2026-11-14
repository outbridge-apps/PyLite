# repeat: 2
# deadline: 120000
# allocmb: 3000
import time, math, decimal, statistics, fractions, random
T = time.perf_counter
out = []
def sec(name, f):
    t = T()
    f()
    out.append("%s=%d" % (name, (T() - t) * 1000))
FL = [i * 0.37 + 0.001 for i in range(200000)]
D = decimal.Decimal
DS = [D("%d.%02d" % (i, i % 100)) for i in range(20000)]
tests = [
    ("sum_float", lambda: [sum(FL) for _ in range(10)]),
    ("sum_mixed", lambda: [sum([1, 2.5, 3] * 30000) for _ in range(5)]),
    ("fsum", lambda: [math.fsum(FL) for _ in range(5)]),
    ("sin_cos", lambda: [math.sin(x) + math.cos(x) + math.tan(x) for x in FL]),
    ("sin_big", lambda: [math.sin(x * 1e6) for x in FL[:50000]]),
    ("hypot", lambda: [math.hypot(x, x + 1) + math.dist((x, 1), (2, x)) for x in FL[:100000]]),
    ("expm1_log1p", lambda: [math.expm1(x / 1e5) + math.log1p(x) for x in FL[:100000]]),
    ("exp_log_pow", lambda: [math.exp(x / 1e5) + math.log(x) + x ** 0.5 + math.pow(x, 1.5) for x in FL[:100000]]),
    ("float_str", lambda: [str(x) for x in FL[:100000]]),
    ("float_fmt", lambda: ["%.3f" % x for x in FL[:100000]] + ["{:.6g}".format(x) for x in FL[:50000]]),
    ("float_parse", lambda: [float("%d.%d" % (i, i)) for i in range(100000)]),
    ("int_str", lambda: [str(i * 1000003) for i in range(100000)] + [int("12345%d" % i) for i in range(100000)]),
    ("bigint_mul", lambda: [(3 ** 2000) * (7 ** 1500) for _ in range(200)]),
    ("bigint_str", lambda: [str(7 ** 3000) for _ in range(50)]),
    ("int_pow_mod", lambda: [pow(i, 65537, 1000000007) for i in range(20000)]),
    ("dec_add_mul", lambda: [a * D("1.07") + a for a in DS]),
    ("dec_div", lambda: [a / D("3.3") for a in DS]),
    ("dec_floordiv_mod", lambda: [(a // D(7), a % D(7), divmod(a, D("2.5"))) for a in DS]),
    ("dec_pow", lambda: [a ** 3 for a in DS[:5000]]),
    ("dec_quantize", lambda: [a.quantize(D("0.1"), rounding=decimal.ROUND_HALF_EVEN) for a in DS]),
    ("dec_str_cmp", lambda: [str(a) for a in DS] + sorted(DS)),
    ("dec_sqrt", lambda: [a.sqrt() for a in DS[1:3000]]),
    ("frac", lambda: [fractions.Fraction(i, 7) + fractions.Fraction(1, i) for i in range(1, 10000)]),
    ("stats", lambda: [statistics.pstdev(FL[i:i + 100]) + statistics.mean(FL[i:i + 100]) for i in range(0, 20000, 100)]),
    ("round", lambda: [round(x, 2) for x in FL[:100000]]),
    ("divmod_float", lambda: [divmod(x, 0.7) for x in FL[:100000]]),
    ("random", lambda: [random.uniform(0, 1) + random.gauss(0, 1) for _ in range(30000)]),
]
for name, f in tests:
    sec(name, f)
print(" ".join(out))
