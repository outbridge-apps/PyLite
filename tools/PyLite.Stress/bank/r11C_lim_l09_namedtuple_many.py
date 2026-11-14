# allocmb: 64
# deadline: 20000
import collections
P = collections.namedtuple("P", "a b c")
l = [P(i, i, i) for i in range(2000000)]
print(len(l))
