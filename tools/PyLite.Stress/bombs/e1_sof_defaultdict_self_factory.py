# deadline: 3000
# allocmb: 256
import collections
d = collections.defaultdict(list)
d.default_factory = d
print(repr(d))
