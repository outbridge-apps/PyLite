# deadline: 20000
# allocmb: 1024
a = slice(0)
for _ in range(100000):
    a = slice(a)
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')
