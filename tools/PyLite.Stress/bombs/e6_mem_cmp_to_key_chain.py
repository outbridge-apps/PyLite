# deadline: 3000
# allocmb: 256
import functools
K = functools.cmp_to_key(lambda a, b: 0)
k = None
for _ in range(15000000):
    k = K(k)
