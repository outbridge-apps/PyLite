# allocmb: 64
# deadline: 20000
import re
s = "ab " * 2000000
n = 0
for m in re.finditer(r"(\w)(\w)", s):
    n += 1
print(n)
