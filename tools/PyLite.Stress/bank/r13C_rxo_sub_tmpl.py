# deadline: 30000
# allocmb: 64
import re
s = "ab" * 1000000
r = re.sub("(a)", r"\g<0>" * 20, s)
print(type(r).__name__, len(r))
