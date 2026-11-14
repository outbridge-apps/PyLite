import time, heapq, bisect, math, random, statistics, hashlib, base64
from fractions import Fraction
t = time.perf_counter()
random.seed(7)
data = [random.randint(0, 10**6) for _ in range(100000)]
top = heapq.nlargest(10, data)
h = []
for x in data[:50000]:
    heapq.heappush(h, x)
small = [heapq.heappop(h) for _ in range(1000)]
srt = sorted(data)
pos = [bisect.bisect_left(srt, x) for x in range(0, 10**6, 1000)]
t1 = time.perf_counter()
mean = statistics.mean(data[:20000])
med = statistics.median(data[:20000])
fs = math.fsum(x / 7 for x in data[:50000])
fr = sum(Fraction(i, i + 1) for i in range(1, 300))
t2 = time.perf_counter()
digs = [hashlib.sha256(("k%d" % i).encode()).hexdigest()[:8] for i in range(20000)]
b64 = [base64.b64encode(("v%d" % i).encode()).decode() for i in range(20000)]
t3 = time.perf_counter()
g = sum(i * i for i in range(200000) if i % 3)
try_count = 0
for i in range(50000):
    try:
        if i % 5 == 0:
            raise KeyError(i)
    except KeyError as e:
        try_count += 1
    finally:
        pass
lam = sorted(range(50000), key=lambda v: (v % 97, -v))[:3]
t4 = time.perf_counter()
print(top[:2], small[:2], pos[:3], round(mean, 3), med, round(fs, 3), fr.numerator % 1000, digs[1], b64[1], g, try_count, lam)
print([round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3), (t3, t4))])
