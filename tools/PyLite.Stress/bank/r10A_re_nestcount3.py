# deadline: 3000
import re
s = "ab" * 250000
n = 0
keep = []
for i in range(300):
    p = re.compile("(?:(?:(?:ab){1,64}){1,64}){1,64}" + "x?" * (i + 1))
    m = p.match(s)
    keep.append(p)
    n += m.end()
print(n, len(keep))
