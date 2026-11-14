# deadline: 60000
# Review 12, row 14: ordinary kept patterns, one search each, are charged as before.
import re
kept = []
n = 0
for i in range(2000):
    p = re.compile(r'id-%d-(\d+)' % i)
    if p.search('id-%d-77' % i):
        n += 1
    kept.append(p)
print(n)
