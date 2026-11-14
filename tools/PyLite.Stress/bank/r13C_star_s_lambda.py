# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
g = lambda n, *x: len(x) if n == 0 else g(n - 1, *a)
f = lambda n: g(n)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
