# allocmb: 64
# deadline: 20000
import collections
d = collections.deque()
for i in range(4000000):
    d.append(i)
print(len(d))
