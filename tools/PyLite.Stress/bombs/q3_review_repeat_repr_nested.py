# deadline: 3000
# allocmb: 1024
import itertools
r = 0
for i in range(200000):
    r = itertools.repeat(r)
try:
    x = repr(r)
except RecursionError:
    print('clean RecursionError')
