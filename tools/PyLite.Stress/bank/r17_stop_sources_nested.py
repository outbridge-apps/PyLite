# deadline: 20000
# Review 12, row 5 follow-up: the handlers for the above are off the frames that nest.
import itertools, heapq
def deep(n, wrap, read):
    it = iter([1, 0, 3])
    for i in range(n):
        it = wrap(it)
    print(read(it))
deep(200000, lambda it: itertools.compress(it, itertools.repeat(1)), list)
deep(200000, lambda it: itertools.compress(itertools.repeat(1), it), next)
deep(125000, lambda it: next(itertools.groupby(it))[1], lambda it: len(list(it)))
deep(75000, heapq.merge, list)
deep(45000, lambda it: (k for k, g in itertools.groupby(it)), list)
