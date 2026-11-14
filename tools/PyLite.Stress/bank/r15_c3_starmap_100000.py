# deadline: 20000
import itertools, operator
it = iter([1, 2, 3])
for i in range(100000):
    it = itertools.starmap(abs, map(lambda v: (v,), it)) if i == 0 else map(abs, it)
print(next(it))
