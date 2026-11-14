import re, time
ln = "2026-11-01 12:00:00 [INFO] user=alice ip=10.0.1.2 took 17ms path=/api/v1/item/5"
pat = re.compile(r"^(\d{4})-(\d\d)-(\d\d) (\d\d):(\d\d):(\d\d) \[(\w+)\] user=(\w+) ip=([\d.]+) took (\d+)ms path=(\S+)$")
t = time.perf_counter()
n = 0
for i in range(100000):
    m = pat.search(ln)
    if m:
        n += 1
print(n, round((time.perf_counter() - t) * 1000))
