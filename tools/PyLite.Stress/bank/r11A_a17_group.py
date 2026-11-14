# allocmb: 32
import re
s = 'a' * 1000000
keep = []
n = 0
for i in range(60):
    p = re.compile(r'(a)(?:\1)*(?:x%d)?' % i)
    n += p.match(s).end()
    keep.append(p)
print(n)
