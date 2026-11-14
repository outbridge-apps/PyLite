# repeat: 2
# deadline: 120000
import time, math
T = time.perf_counter
out = []
for mag in (1.0, 1e3, 1e5, 1e6, 1e9, 1e15, 1e300):
    xs = [mag * (1 + i * 1e-6) for i in range(30000)]
    t = T()
    s = 0.0
    for x in xs:
        s += math.sin(x) + math.cos(x)
    out.append("%g=%d" % (mag, (T() - t) * 1000))
print(" ".join(out), math.sin(1e22), math.cos(1e300), math.tan(1e10))
