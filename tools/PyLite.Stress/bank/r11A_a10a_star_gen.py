# allocmb: 64
a = [0] * 1000000
def gen(*args):
    yield len(args)
keep = []
for i in range(60):
    keep.append(gen(*a))
print(len(keep))
