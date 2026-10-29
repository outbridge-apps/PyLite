# deadline: 20000
# allocmb: 1024
import bisect
l = []
for i in range(200000):
    bisect.insort(l, -i)
print(len(l))
