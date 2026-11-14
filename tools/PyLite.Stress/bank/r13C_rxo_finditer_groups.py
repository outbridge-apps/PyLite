# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = list(re.finditer("(a)(b)", s))
print(type(r).__name__, len(r))
