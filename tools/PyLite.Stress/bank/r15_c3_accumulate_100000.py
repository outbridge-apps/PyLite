# deadline: 20000
import itertools, operator
it = iter([1, 2, 3])
for i in range(100000):
    it = itertools.accumulate(it, max)
print(next(it))
