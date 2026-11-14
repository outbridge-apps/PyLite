# deadline: 60000
# Review 12, row 5: nested filter(None) and filterfalse(None), no native frame of their own per level.
import itertools
for N in (100000, 200000):
    it = iter([0, 0, 5, 0, 7])
    for i in range(N):
        it = filter(None, it)
    try:
        print(N, 'filter', list(it))
    except Exception as e:
        print(N, 'filter', type(e).__name__)
    it = iter([0, 0, 5, 0, 7])
    for i in range(N):
        it = itertools.filterfalse(None, it)
    try:
        print(N, 'filterfalse', list(it))
    except Exception as e:
        print(N, 'filterfalse', type(e).__name__)
