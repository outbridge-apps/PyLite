# allocmb: 16
# deadline: 120000
# Review 17: behind (?:){1} or (?=a){1}, which .NET keeps as they are, a{1500} keeps its 1,500-character prefix and
# table in each regex; the prefix walk stopped there and charged none (1,500 kept grew the process to 96 MB under
# 16 MB). The limit is met now.
import re
kept = []
A = 'a' * 1500
for i in range(1500):
    p = ('(?:){1}a{1500}k%d' if i % 2 else '(?=a){1}a{1500}k%d') % i
    r = re.compile(p)
    s = A + 'k%d' % i
    r.search(s); r.match(s); r.fullmatch(s)
    kept.append(r)
print(len(kept))
