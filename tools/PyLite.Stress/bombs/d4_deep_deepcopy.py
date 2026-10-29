# deadline: 20000
# allocmb: 1024
import copy
l = []
for _ in range(100000):
    l = [l]
try:
    print(len(copy.deepcopy(l)))
except RecursionError as e:
    print('clean RecursionError')
