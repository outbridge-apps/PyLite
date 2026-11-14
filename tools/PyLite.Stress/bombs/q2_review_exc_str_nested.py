# deadline: 3000
# allocmb: 1024
e = ValueError(1)
for i in range(200000):
    e = ValueError(e)
try:
    x = str(e)
except RecursionError:
    print('clean RecursionError')
