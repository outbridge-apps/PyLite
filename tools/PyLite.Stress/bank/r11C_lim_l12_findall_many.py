# allocmb: 64
# deadline: 20000
import re
s = "ab " * 1000000
l = re.findall(r"\w+", s)
print(len(l))
