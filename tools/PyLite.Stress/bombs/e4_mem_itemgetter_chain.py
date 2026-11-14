# deadline: 3000
# allocmb: 256
import operator
g = None
for _ in range(5000000):
    g = operator.itemgetter(g)
