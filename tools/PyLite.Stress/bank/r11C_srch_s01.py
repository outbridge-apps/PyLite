# deadline: 2000
import time
t0 = time.perf_counter()
h = "a" * 8000000
n = "a" * 4000000 + "b"
r = h.find(n)
print(repr(r)[:20], time.perf_counter() - t0)
h2 = "ab" * 4000000
n2 = "ab" * 2000000 + "a" * 3
r2 = h2.find(n2)
print(repr(r2)[:20], time.perf_counter() - t0)
