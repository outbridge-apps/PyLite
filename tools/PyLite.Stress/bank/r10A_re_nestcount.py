# deadline: 30000
import re
s = "ab" * 250000
n = 0
for i in range(40):
    # each pattern loops a group 64^3 times in all, but every count is <= 64: not LongLoop on HEAD
    p = re.compile("(?:(?:(?:ab){1,64}){1,64}){1,64}" + "x?" * (i + 1))
    m = p.match(s)
    n += m.end()
print(n)
