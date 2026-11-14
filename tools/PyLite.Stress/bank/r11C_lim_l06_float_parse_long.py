# allocmb: 64
# deadline: 20000
import time
t0 = time.perf_counter()
n = 0
for i in range(40):
    n += float("1" * 500000 + "e-500000") > 0
print(n, time.perf_counter() - t0)
