# deadline: 8000
a = list(range(1000000))
a[0] = -1
def h(n):
    # min(*a, key=...) holds its 1M-entry args array while the key recurses into the next min(*a)
    return min(*a, key=lambda v, n=n: (h(n - 1) if v < 0 and n > 0 else v))
try:
    print(h(150))
except Exception as e:
    print(type(e).__name__, e)
