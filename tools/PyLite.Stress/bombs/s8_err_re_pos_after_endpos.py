# deadline: 3000
# allocmb: 256
import re
p = re.compile('a')
print(p.search('aaa', 2, 1), p.findall('aaa', 2, 1), list(p.finditer('aaa', 2, 1)))
