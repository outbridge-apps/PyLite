# deadline: 20000
# allocmb: 1024
from collections import deque
a = deque()
for _ in range(100000):
    a = deque([a])
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')
