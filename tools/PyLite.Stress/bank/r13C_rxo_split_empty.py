# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = re.split("", s)
print(type(r).__name__, len(r))
