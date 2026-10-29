# datadepth: 1000000
# deadline: 20000
# allocmb: 1024
s = slice(1)
for i in range(200000):
    s = slice(s)
try:
    str(s)
except RecursionError:
    print('clean RecursionError')
