# deadline: 30000
# allocmb: 64
a = list(range(300000))
d = {str(i): i for i in range(100000)}
def f(n):
    if n == 0:
        return 0
    return max(*a[:3], key=lambda x: f(n - 1) + len(a) if x == 0 else 0)
try:
    print(f(150))
except RecursionError as e:
    print("RE")
