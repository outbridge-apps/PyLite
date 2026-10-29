# deadline: 20000
# allocmb: 1024
import functools
p = functools.partial(print)
for i in range(100000):
    p = functools.partial(p, 1)
print(len(p.args))
