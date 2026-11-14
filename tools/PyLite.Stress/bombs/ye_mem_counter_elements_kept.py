# deadline: 3000
# allocmb: 256
from collections import Counter
c = Counter(range(1000000))
its = []
for i in range(12):
    its.append(c.elements())
    next(its[-1])
print(len(its))
