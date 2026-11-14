# allocmb: 64
import functools
a = [0] * 1000000
keep = []
for i in range(60):
    keep.append(functools.partial(max, *a))
print(len(keep))
