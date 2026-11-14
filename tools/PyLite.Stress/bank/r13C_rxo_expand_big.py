# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = [m.expand(r"\1" * 1000) for m in re.finditer("(ab)", s[:20000])]
print(type(r).__name__, len(r))
