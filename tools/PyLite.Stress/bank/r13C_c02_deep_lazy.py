# deadline: 20000
import itertools, functools, operator, collections
N = 100000
out = []
def t(name, mk, use=next):
    try:
        it = mk()
        r = use(it)
        out.append(name + '=' + repr(r)[:60])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:60])

def nest(f):
    def mk():
        it = iter([1, 2, 3])
        for i in range(N):
            it = f(it)
        return it
    return mk

t('map', nest(lambda it: map(abs, it)))
t('filter', nest(lambda it: filter(None, it)))
t('zip', nest(lambda it: zip(it)))
t('enumerate', nest(lambda it: map(operator.itemgetter(1), enumerate(it))))
t('chain', nest(lambda it: itertools.chain(it)))
t('chain_from', nest(lambda it: itertools.chain.from_iterable([it])))
t('islice', nest(lambda it: itertools.islice(it, 5)))
t('accumulate', nest(lambda it: itertools.accumulate(it)))
t('starmap', nest(lambda it: itertools.starmap(abs, zip(it))))
t('takewhile', nest(lambda it: itertools.takewhile(bool, it)))
t('dropwhile', nest(lambda it: itertools.dropwhile(operator.not_, it)))
t('compress', nest(lambda it: itertools.compress(it, itertools.repeat(1))))
t('filterfalse', nest(lambda it: itertools.filterfalse(operator.not_, it)))
t('zip_longest', nest(lambda it: map(operator.itemgetter(0), itertools.zip_longest(it))))
t('pairwise', nest(lambda it: map(operator.itemgetter(0), itertools.pairwise(itertools.chain(it, [9])))))
t('tee', nest(lambda it: itertools.tee(it, 1)[0]))
t('genexp', nest(lambda it: (x for x in it)))
t('iter_call', nest(lambda it: iter(it.__next__, None)))
t('reversed_list', nest(lambda it: iter(list(it)) ))
t('cycle', nest(lambda it: itertools.cycle(it)))
t('batched', nest(lambda it: map(operator.itemgetter(0), itertools.batched(it, 1))))
t('groupby', nest(lambda it: map(operator.itemgetter(0), itertools.groupby(it))))
t('map_sum', nest(lambda it: map(abs, it)), sum)
t('map_list', nest(lambda it: map(abs, it)), list)
t('map_sorted', nest(lambda it: map(abs, it)), sorted)
t('map_tuple', nest(lambda it: map(abs, it)), tuple)
t('map_set', nest(lambda it: map(abs, it)), set)
t('map_in', nest(lambda it: map(abs, it)), lambda it: 3 in it)
t('map_any', nest(lambda it: map(abs, it)), any)
t('map_max', nest(lambda it: map(abs, it)), max)
t('map_join', nest(lambda it: map(str, it)), ''.join)
t('map_dict', nest(lambda it: map(lambda x: (x, x), it)), dict)
t('map_deque', nest(lambda it: map(abs, it)), collections.deque)
t('map_counter', nest(lambda it: map(abs, it)), collections.Counter)
t('map_reduce', nest(lambda it: map(abs, it)), lambda it: functools.reduce(operator.add, it))
t('map_unpack', nest(lambda it: map(abs, it)), lambda it: [*it])
t('map_star_call', nest(lambda it: map(abs, it)), lambda it: max(*it))
t('map_enumerate_list', nest(lambda it: map(abs, it)), lambda it: list(enumerate(it)))
t('map_zip_list', nest(lambda it: map(abs, it)), lambda it: list(zip(it, it)))
t('map_str_join', nest(lambda it: map(str, it)), lambda it: ','.join(it))
t('map_bytes', nest(lambda it: map(abs, it)), bytes)
t('map_for', nest(lambda it: map(abs, it)), lambda it: [x for x in it])
t('map_minmax_key', nest(lambda it: map(abs, it)), lambda it: min(it, key=abs))
t('map_heapq', nest(lambda it: map(abs, it)), lambda it: __import__('heapq').nlargest(2, it))
t('map_statistics', nest(lambda it: map(abs, it)), lambda it: __import__('statistics').mean(it))
t('map_fsum', nest(lambda it: map(float, it)), lambda it: __import__('math').fsum(it))
t('map_prod', nest(lambda it: map(abs, it)), lambda it: __import__('math').prod(it))
t('map_sumprod', nest(lambda it: map(abs, it)), lambda it: __import__('math').sumprod(it, [1, 1, 1]))
t('map_csv', nest(lambda it: map(lambda x: str(x) + ',a', it)), lambda it: list(__import__('csv').reader(it)))
print(chr(10).join(out))
