# deadline: 30000
import math, time
def t(name, f):
    t0 = time.perf_counter()
    try:
        print(name + '=' + repr(f())[:100], round(time.perf_counter() - t0, 3))
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80], round(time.perf_counter() - t0, 3))
t('perm_none', lambda: [math.perm(5), math.perm(5, 6), math.comb(5, 6)])
t('comb_huge_k', lambda: math.comb(2 ** 70, 2).bit_length())
t('perm_huge', lambda: math.perm(2 ** 70, 3).bit_length())
t('lcm_5k', lambda: math.lcm(*range(1, 5000)).bit_length())
t('lcm_20k', lambda: math.lcm(*range(1, 20000)).bit_length())
t('gcd_big', lambda: math.gcd(math.factorial(20000), 7 ** 5000).bit_length())
def loop_lcm():
    x = 1
    for i in range(1, 20000):
        x = x * i // math.gcd(x, i)
    return x.bit_length()
t('lcm_loop_20k', loop_lcm)
