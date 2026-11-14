import re, time
# many distinct patterns with \w / \b / IGNORECASE, each used once (match, search, fullmatch) on a short risky subject
s = 'cafe' + chr(0x301) + ' k1=v'
t = time.perf_counter()
hits = 0
for i in range(6000):
    key = 'k%05d' % i
    p = re.compile(key + r'=(\w+)\s+\w+=(\w+)\b', re.I)
    if p.search(s):
        hits += 1
    if p.match(s):
        hits += 1
    if p.fullmatch(s):
        hits += 1
print(hits, round(time.perf_counter() - t, 2))
