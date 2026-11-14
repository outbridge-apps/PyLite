# allocmb: 64
# deadline: 20000
l = sorted(range(4000000), key=lambda x: (x % 7, str(x)))
print(len(l))
