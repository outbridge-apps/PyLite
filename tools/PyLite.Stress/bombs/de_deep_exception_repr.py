# deadline: 20000
# allocmb: 1024
e = ValueError(1)
for _ in range(100000):
    e = ValueError(e)
try:
    print(len(repr(e)))
except RecursionError as e:
    print('clean RecursionError')
