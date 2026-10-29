# deadline: 20000
# allocmb: 1024
l = []
for _ in range(100000):
    l = [l]
try:
    print(len(repr(l)))
except RecursionError as e:
    print('clean RecursionError')
