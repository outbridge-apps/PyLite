# repeat: 2
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
by_level = Counter()
by_user = defaultdict(int)
slow = 0
for ln in lines:
    m = pat.match(ln)
    if m:
        by_level[m.group(7)] += 1
        by_user[m.group(8)] += int(m.group(10))
        if int(m.group(10)) > 900:
            slow += 1
words = Counter()
for ln in lines[:5000]:
    for w in re.findall(r"\b[a-z]{4,}\b", ln):
        words[w] += 1
errs = [ln for ln in lines if re.search(r"\bERROR\b", ln)]
red = re.sub(r"ip=\d+\.\d+\.\d+\.\d+", "ip=x", lines[0])
spl = re.split(r"\s+", lines[1])
t1 = time.perf_counter()
print(sorted(by_level.items()), sorted(by_user.items())[:2], slow, words.most_common(3), len(errs), red, len(spl))
print("T", round((t1 - t0) * 1000))
