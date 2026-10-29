# deadline: 20000
# allocmb: 1024
from functools import partial
def f(*a): pass
p = partial(f)
for _ in range(100000):
    p = partial(f, p)
try:
    print(len(repr(p)))
except RecursionError as e:
    print('clean RecursionError')
