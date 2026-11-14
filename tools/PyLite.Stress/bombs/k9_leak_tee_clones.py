# deadline: 3000
# allocmb: 1024
import itertools
ts = itertools.tee(range(5000), 1500000)
x = list(ts[0])
