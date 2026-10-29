# deadline: 20000
# allocmb: 1024
import re
p = re.compile('a' * 1000000)
print(p.match('a' * 1000000) is not None)
