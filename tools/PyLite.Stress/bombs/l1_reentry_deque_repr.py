# datadepth: 1000000
# deadline: 20000
# allocmb: 1024
from collections import deque
d = deque()
for i in range(200000):
    d = deque([d])
try:
    repr(d)
except RecursionError:
    print('clean RecursionError')
