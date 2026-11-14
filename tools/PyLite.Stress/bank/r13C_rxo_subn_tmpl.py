# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = re.subn("(a)(b)", r"\2\1\2\1\2\1\2\1\2\1", s)
print(type(r).__name__, len(r))
