# deadline: 20000
import itertools
it = iter([1, 2, 3])
for i in range(100000):
    it = map(max, it, itertools.repeat(0))
print(next(it), next(it), next(it), next(it, None))
