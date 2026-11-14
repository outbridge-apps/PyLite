# deadline: 2000
import time
t0 = time.perf_counter()
h = b"a" * 8000000
n = b"a" * 4000000 + b"b"
r = h.count(n)
print(repr(r)[:20], time.perf_counter() - t0)
