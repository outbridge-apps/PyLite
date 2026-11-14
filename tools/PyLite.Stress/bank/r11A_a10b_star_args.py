# allocmb: 64
a = [0] * 1000000
def f(*args):
    return args
keep = []
for i in range(60):
    keep.append(f(*a))
print(len(keep))
