# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
import functools
def h(n, *args):
    return len(args) if n == 0 else functools.partial(h, n - 1, *a)()
f = lambda n: h(n)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
