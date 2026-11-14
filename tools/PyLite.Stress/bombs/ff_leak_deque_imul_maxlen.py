# deadline: 3000
# allocmb: 256
from collections import deque
d = deque([0] * 2000000, maxlen=2000000)
while True:
    d *= 10**18
