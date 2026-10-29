# deadline: 20000
# allocmb: 1024
from collections import deque
d = deque(range(1000000))
d.rotate(500000)
print(d[0])
