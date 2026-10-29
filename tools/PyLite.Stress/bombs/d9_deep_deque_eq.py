# deadline: 20000
# allocmb: 1024
from collections import deque
a = deque(); b = deque()
for _ in range(100000):
    a = deque([a]); b = deque([b])
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')
