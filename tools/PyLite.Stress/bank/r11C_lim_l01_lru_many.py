# allocmb: 64
# deadline: 20000
import functools
@functools.lru_cache(maxsize=None)
def f(x):
    return x
for i in range(3000000):
    f(i)
print(f.cache_info())
