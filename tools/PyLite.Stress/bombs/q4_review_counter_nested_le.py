# deadline: 3000
# allocmb: 1024
from collections import Counter
a = Counter()
b = Counter()
for i in range(100000):
    x = Counter()
    x['k'] = a
    a = x
    y = Counter()
    y['k'] = b
    b = y
try:
    x = a <= b
except RecursionError:
    print('clean RecursionError')
