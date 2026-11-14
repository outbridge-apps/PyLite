# Review 13, row 30: a script iterator that ends and yields again, through every consumer (CPython 3.14.2 answers).
import itertools, heapq, csv
def t(label, f):
    try:
        print(label, f())
    except BaseException as e:
        print(label, 'EXC', type(e).__name__, e.args)
class R:
    def __init__(s, tup=False): s.i = 0; s.tup = tup
    def __iter__(s): return s
    def __next__(s):
        s.i += 1
        if s.i == 3 or s.i == 6 or s.i > 8:
            raise StopIteration
        return (s.i, s.i) if s.tup else s.i
def rs(f, tup=False):
    r = R(tup)
    it = f(r)
    return (list(it), list(it), list(it))
t('islice', lambda: rs(lambda r: itertools.islice(r, 10)))
t('islice2', lambda: rs(lambda r: itertools.islice(r, 1, None)))
t('islice3', lambda: rs(lambda r: itertools.islice(r, 0, 4, 2)))
t('pairwise', lambda: rs(lambda r: itertools.pairwise(r)))
t('takewhile', lambda: rs(lambda r: itertools.takewhile(lambda x: True, r)))
t('dropwhile', lambda: rs(lambda r: itertools.dropwhile(lambda x: x < 2, r)))
t('starmap', lambda: rs(lambda r: itertools.starmap(lambda a, b: a + b, r), True))
t('batched', lambda: rs(lambda r: itertools.batched(r, 1)))
t('zip_longest', lambda: rs(lambda r: itertools.zip_longest(r)))
t('groupby', lambda: rs(lambda r: map(lambda kg: kg[0], itertools.groupby(r))))
t('filterfalse', lambda: rs(lambda r: itertools.filterfalse(lambda x: x > 100, r)))
t('accumulate', lambda: rs(lambda r: itertools.accumulate(r, initial=0)))
t('merge', lambda: rs(lambda r: heapq.merge(r)))
t('csv', lambda: rs(lambda r: csv.reader(map(str, r))))
t('zip2', lambda: rs(lambda r: zip(r, itertools.count())))
t('enum', lambda: rs(lambda r: enumerate(r, 5)))
t('chain2', lambda: rs(lambda r: itertools.chain([0], r)))
t('compress', lambda: rs(lambda r: itertools.compress(itertools.count(), r)))
def tee2():
    a, b = itertools.tee(R())
    return (list(a), list(b), list(a), list(b))
t('tee', tee2)
def cyc():
    return list(itertools.islice(itertools.cycle(R()), 7))
t('cycle', cyc)
def sums():
    r = R()
    return (sum(r), sum(r), sum(r), sum(r))
t('sum', sums)
def dictz():
    r = R()
    return (dict(zip(r, r)), dict(zip(r, r)))
t('dictzip', dictz)
def strjoin():
    r = map(str, R())
    return (','.join(r), ','.join(r))
t('join', strjoin)
def maxs():
    r = R()
    return (max(r), max(r, default=-1), max(r, default=-1))
t('max', maxs)
def anys():
    r = R()
    return (any(x > 5 for x in r), list(r))
t('any', anys)
