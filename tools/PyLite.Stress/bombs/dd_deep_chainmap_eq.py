# deadline: 20000
# allocmb: 1024
from collections import ChainMap
a = ChainMap({}); b = ChainMap({})
for _ in range(100000):
    a = ChainMap({'k': a}); b = ChainMap({'k': b})
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')
