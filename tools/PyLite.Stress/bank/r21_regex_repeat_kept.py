# allocmb: 16
# deadline: 60000
# Review 15: a pattern that starts with c{m} keeps m c's and a Boyer-Moore table over them, 6 B a repeat, each regex
# of it; charged as the characters it is written in, 5,000 kept and used every way grew the process 278 MB under a
# 64 MB limit. Held now, the limit is met.
import re
keep = []
s = 'a' * 1500 + 'x'
n = 0
for i in range(5000):
    p = re.compile('a{1500}x%d' % i)
    keep.append(p)
    if p.search(s): n += 1
    if p.match(s): n += 1
    if p.fullmatch(s): n += 1
print(len(keep), n)
