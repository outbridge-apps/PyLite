# deadline: 3000
# allocmb: 256
import re
p = re.compile('a')
s = 'b' * 4000000
its = []
for i in range(60):
    it = p.finditer(s, 0, len(s) - 1 - i)
    next(it, None)
    its.append(it)
