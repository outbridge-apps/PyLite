# Review 12, row 5 follow-up: a StopIteration out of __getitem__, a selector's __bool__ or a key's __eq__ ends the
# iterator that met it, under filter(None, ...) too; heapq.merge raises it.
import itertools, heapq
class Seq:
    def __getitem__(self, i):
        if i == 3:
            raise StopIteration('G')
        return i
class Sub(StopIteration):
    pass
class Seq2:
    def __getitem__(self, i):
        if i == 2:
            raise Sub('G2')
        return i + 1
class Seq3:
    def __getitem__(self, i):
        if i == 2:
            raise KeyError('k')
        return i + 1
class B:
    def __init__(self, n):
        self.n = n
    def __bool__(self):
        if self.n == 2:
            raise StopIteration('B')
        return True
class K:
    def __eq__(self, o):
        raise StopIteration('E')
    def __hash__(self):
        return 1
class L:
    def __init__(self, n):
        self.n = n
    def __lt__(self, o):
        raise StopIteration('L')
def t(label, fn):
    try:
        print(label, fn())
    except BaseException as e:
        print(label, type(e).__name__, e)
def unpack():
    a, b, c = Seq()
    return (a, b, c)
def twice():
    it = iter(Seq())
    return (list(it), list(it))
def third():
    it = filter(None, Seq())
    return (next(it), next(it), next(it, 'end'))
t('list', lambda: list(Seq()))
t('filter', lambda: list(filter(None, Seq())))
t('filterfalse', lambda: list(itertools.filterfalse(None, Seq())))
t('chain', lambda: list(filter(None, itertools.chain([1], Seq(), [9]))))
t('enumerate', lambda: list(enumerate(Seq())))
t('batched', lambda: list(itertools.batched(Seq(), 2)))
t('in', lambda: (2 in Seq(), 7 in Seq()))
t('unpack', unpack)
t('max', lambda: max(Seq()))
t('twice', twice)
t('third', third)
t('next', lambda: next(filter(None, Seq2())) + next(iter(Seq2())))
t('subclass', lambda: list(Seq2()))
t('keyerror', lambda: list(Seq3()))
t('keyerror filter', lambda: list(filter(None, Seq3())))
sel = [B(1), B(1), B(2), B(1)]
t('compress', lambda: list(itertools.compress([1, 2, 3, 4], sel)))
t('compress filter', lambda: list(filter(None, itertools.compress([1, 2, 3, 4], sel))))
t('compress filterfalse', lambda: list(itertools.filterfalse(None, itertools.compress([1, 2, 3, 4], sel))))
t('groupby', lambda: len(list(itertools.groupby([K(), K()]))))
t('groupby filter', lambda: len(list(filter(None, itertools.groupby([K(), K()])))))
t('grouper', lambda: [len(list(g)) for k, g in itertools.groupby([K(), K()])])
t('merge', lambda: type(list(heapq.merge([L(1), L(3)], [L(2)]))).__name__)
t('merge filter', lambda: type(list(filter(None, heapq.merge([L(1), L(3)], [L(2)])))).__name__)
