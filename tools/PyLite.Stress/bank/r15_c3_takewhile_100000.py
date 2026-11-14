# deadline: 20000
import itertools, operator
it = iter([1, 2, 3])
for i in range(100000):
    it = itertools.takewhile(abs, it)
print(next(it))
