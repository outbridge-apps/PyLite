# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, functools
t = time.perf_counter()
f = functools.lru_cache(maxsize=128)(lambda v: v * 2)
for i in range(100000): f(i % 200)
print("ms=%d" % ((time.perf_counter() - t) * 1000))
