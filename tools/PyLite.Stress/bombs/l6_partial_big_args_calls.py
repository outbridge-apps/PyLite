# deadline: 3000
# allocmb: 1024
import functools
p = functools.partial(max, *range(1000000))
n = 0
while True:
    n = p()
