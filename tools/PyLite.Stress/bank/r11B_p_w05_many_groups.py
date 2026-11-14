import re, time
# a 30-column fixed-width export, one group per column, 250,000 records
rec = "".join(chr(65 + i % 26) for i in range(30)) + "\n"
data = rec * 250000
t = time.perf_counter()
pat = re.compile("(.)" * 30)
n = 0
for m in pat.finditer(data):
    n += 1
print(n, m.group(30), round((time.perf_counter() - t) * 1000))
