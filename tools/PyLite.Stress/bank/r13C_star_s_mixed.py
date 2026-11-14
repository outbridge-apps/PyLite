# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
def f(n, *args, **kw):
    if n == 0:
        return len(args) + len(kw)
    return f(n - 1, *a, **d)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
