# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = re.escape(s.replace("a", "."))
print(type(r).__name__, len(r))
