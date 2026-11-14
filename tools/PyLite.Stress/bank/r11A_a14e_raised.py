# allocmb: 2048
# deadline: 20000
import re, time
# per batch a fresh keyword alternation (about 3,000 chars), used once and dropped
t = time.perf_counter()
hits = 0
for b in range(1000):
    kws = ['kw%d_%d' % (b, i) for i in range(400)]
    p = re.compile(r'\b(?:' + '|'.join(kws) + r')\b', re.I)
    if p.search('nothing here kw%d_7 ok' % b):
        hits += 1
print(hits, round(time.perf_counter() - t, 2))
