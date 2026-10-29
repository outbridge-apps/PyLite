# deadline: 20000
# allocmb: 1024
l = []; m = []
for _ in range(100000):
    l = [l]; m = [m]
try:
    print(l == m)
except RecursionError as e:
    print('clean RecursionError')
