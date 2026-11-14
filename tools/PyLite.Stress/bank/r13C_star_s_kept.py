# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
keep = []
def f(n, *args):
    keep.append(args)
    if n == 0:
        return len(keep)
    return f(n - 1, *a)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
