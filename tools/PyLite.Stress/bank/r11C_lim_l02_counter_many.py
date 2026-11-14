# allocmb: 64
# deadline: 20000
import collections
c = collections.Counter()
for i in range(3000000):
    c[i] += 1
print(len(c))
