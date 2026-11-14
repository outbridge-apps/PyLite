# deadline: 20000
a = [0] * 1000000
def f(n, *args):
    if n == 0:
        return len(args)
    return f(n - 1, *a)
print(f(150))
