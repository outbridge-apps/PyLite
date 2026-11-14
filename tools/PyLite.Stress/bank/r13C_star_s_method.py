# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
class C:
    def m(self, n, *args):
        if n == 0:
            return len(args)
        return self.m(n - 1, *a)
f = lambda n: C().m(n)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
