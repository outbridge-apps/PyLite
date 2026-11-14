import re, time
# fixed-width bank statement lines: 20 fields, parsed by one pattern with a group per field
rec = "".join("%02d" % (i % 100) for i in range(20)) + "\n"
data = rec * 330000
t = time.perf_counter()
pat = re.compile("(..)" * 20)
n = 0
s = 0
for m in pat.finditer(data):
    n += 1
    s += int(m.group(20))
print(n, s, round((time.perf_counter() - t) * 1000))
