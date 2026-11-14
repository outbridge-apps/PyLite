# deadline: 3000
# allocmb: 256
import collections
a = collections.defaultdict(None)
for i in range(500000):
    b = collections.defaultdict(None)
    b.default_factory = a
    a = b
print(len(repr(a)))
