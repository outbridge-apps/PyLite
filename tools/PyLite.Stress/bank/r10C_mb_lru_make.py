# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, functools
t = time.perf_counter()
for i in range(20000): functools.lru_cache(maxsize=128)(lambda v: v * 2)(i)
print("ms=%d" % ((time.perf_counter() - t) * 1000))
