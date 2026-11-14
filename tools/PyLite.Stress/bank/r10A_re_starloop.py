# deadline: 30000
import re
s = "ab" * 250000
n = 0
for i in range(40):
    p = re.compile("(?:ab)*" + "x?" * (i + 1))
    m = p.match(s)
    n += m.end()
print(n)
