# datadepth: 1000000
# deadline: 20000
# allocmb: 1024
e = ValueError()
for i in range(200000):
    e = ValueError(e)
try:
    repr(e)
except RecursionError:
    print('clean RecursionError')
