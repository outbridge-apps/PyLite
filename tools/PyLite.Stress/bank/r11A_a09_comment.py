import re
s = 'ab' * 500000
keep = []
n = 0
for i in range(60):
    p = re.compile('(?:(?:(?:ab)(?#c){1,64})(?#c){1,64})(?#c){1,64}(?:x%d)?' % i)
    n += p.match(s).end()
    keep.append(p)
print(n)
