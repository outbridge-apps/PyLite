# deadline: 20000
# allocmb: 1024
t = ()
for _ in range(100000):
    t = (t,)
try:
    print(hash(t) is not None)
except RecursionError as e:
    print('clean RecursionError')
