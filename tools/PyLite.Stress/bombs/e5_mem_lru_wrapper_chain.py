# deadline: 3000
# allocmb: 256
import functools
d = functools.lru_cache(maxsize=0)
f = len
for _ in range(15000000):
    f = d(f)
