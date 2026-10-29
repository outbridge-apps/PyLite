# deadline: 20000
# allocmb: 1024
class N:
    def __init__(self, c):
        self.c = c
a = None
for _ in range(100000):
    a = N(a)
try:
    print(len(repr(a)))
except RecursionError as e:
    print('clean RecursionError')
