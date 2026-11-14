# deadline: 3000
# allocmb: 256
import re
p = re.compile('a')
s = 'a' * 1000000
kept = [p.search(s, 0, len(s) - 1 - i) for i in range(400)]
