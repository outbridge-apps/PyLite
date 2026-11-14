# deadline: 6000
# calldepth: 60
a = [0] * 4000000
a[0] = -1
def h(n):
    return min(*a, key=lambda v, n=n: (h(n - 1) if v < 0 and n > 0 else v))
try:
    print(h(150))
except Exception as e:
    print(type(e).__name__, e)
