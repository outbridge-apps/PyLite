# repeat: 1
import re, time
from collections import Counter, defaultdict
t0 = time.perf_counter()
levels = ["INFO", "WARN", "ERROR", "DEBUG"]
users = ["alice", "bob", "carol", "dave", "erin"]
lines = []
for i in range(20000):
    lines.append("2026-11-%02d 12:%02d:%02d [%s] user=%s ip=10.0.%d.%d took %dms path=/api/v1/item/%d" % (
        i % 28 + 1, i % 60, (i * 7) % 60, levels[i % 4], users[i % 5], i % 256, (i * 3) % 256, i % 997, i))
pat = re.compile(r"^(\d{4})-(\d\d)-(\d\d) (\d\d):(\d\d):(\d\d) \[(\w+)\] user=(\w+) ip=([\d.]+) took (\d+)ms path=(\S+)$")
t1 = time.perf_counter()
n = 0
for ln in lines[:5000]:
    n += len(re.findall(r"\b[a-z]{4,}\b", ln))
print("findall", n, round((time.perf_counter()-t1)*1000))
