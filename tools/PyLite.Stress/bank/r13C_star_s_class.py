# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
class C:
    def __init__(self, n, *args):
        self.r = len(args) if n == 0 else C(n - 1, *a).r
f = lambda n: C(n).r
try:
    print(f(150))
except RecursionError as e:
    print("RE")
