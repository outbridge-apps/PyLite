# deadline: 20000
# allocmb: 1024
import re
p = re.compile('[' + chr(176) + ']' * 100000)
print(p is not None)
