import re, time
from collections import Counter
# a host passes a day's access log; the script totals per user and level
parts = []
for i in range(1000):
    parts.append("2026-11-%02d 12:%02d:%02d [%s] user=%s ip=10.0.%d.%d took %dms path=/api/v1/item/%d\n" % (
        i % 28 + 1, i % 60, (i * 7) % 60, ["INFO", "WARN", "ERROR", "DEBUG"][i % 4], ["alice", "bob", "carol"][i % 3], i % 256, (i * 3) % 256, i % 997, i))
log = "".join(parts) * 180
t = time.perf_counter()
pat = re.compile(r"^(\d{4})-(\d\d)-(\d\d) (\d\d):(\d\d):(\d\d) \[(\w+)\] user=(\w+) ip=([\d.]+) took (\d+)ms path=(\S+)$", re.M)
lv = Counter()
tot = Counter()
n = 0
for m in pat.finditer(log):
    n += 1
    lv[m.group(7)] += 1
print(n, sorted(lv.items()), round((time.perf_counter() - t) * 1000))
