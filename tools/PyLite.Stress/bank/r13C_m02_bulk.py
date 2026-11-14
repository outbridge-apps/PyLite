import itertools, functools, collections, hashlib, zlib, io, re, datetime, decimal, fractions, typing, string, operator, uuid, json, csv, struct, copy
N = 50000
def meas(name, build, n=N):
    h0 = heap(); c0 = charged()
    try:
        keep = build()
    except Exception as e:
        print(name, 'EXC', type(e).__name__, str(e)[:60])
        return None
    h1 = heap(); c1 = charged()
    r = (h1 - h0) // n
    c = (c1 - c0) // n
    print(name, 'real', r, 'charged', c, 'ratio', round(r / c, 2) if c > 0 else 'inf')
    return keep

class E(Exception):
    pass
def raiser(i):
    try:
        raise E(i)
    except E as e:
        return e
def deep_raiser(i):
    def f(k):
        if k == 0:
            raise E(i)
        f(k - 1)
    try:
        f(20)
    except E as e:
        return e
k = []
def tee_buf():
    a, b = itertools.tee(range(9000))
    for x in a:
        pass
    return b
k = []
k.append(meas('tee_buffer', tee_buf, 9000))
def tee_many():
    return itertools.tee(range(10), 1000)
k.append(meas('tee_1000', tee_many, 1000))
def gb():
    l = []
    for kk, g in itertools.groupby(range(N), lambda x: x // 2):
        l.append(g)
    return l
k.append(meas('groupby_groups', gb, N // 2))
@functools.lru_cache(maxsize=None)
def cached(x):
    return x
def fill():
    for i in range(N):
        cached(i)
    return cached
k.append(meas('lru_entries', fill))
@functools.cache
def cached2(x, y='a'):
    return x
def fill2():
    for i in range(N):
        cached2(i, y='b')
    return cached2
k.append(meas('cache_kw_entries', fill2))
k.append(meas('cmp_to_key_objs', lambda: [functools.cmp_to_key(lambda a, b: 0)(i) for i in range(N)]))
k.append(meas('struct_obj', lambda: [struct.Struct('i') for i in range(N)]))
k.append(meas('csv_writer', lambda: [csv.writer(io.StringIO()) for i in range(5000)], 5000))
k.append(meas('csv_reader', lambda: [csv.reader([]) for i in range(5000)], 5000))
k.append(meas('compiled_short_re_distinct', lambda: [re.compile('a%d' % i) for i in range(500)], 500))
k.append(meas('decimal_ctx', lambda: [decimal.Context() for i in range(N)]))
k.append(meas('fraction', lambda: [fractions.Fraction(i, 7) for i in range(N)]))
k.append(meas('json_loads_obj', lambda: json.loads('[' + ','.join(['{"a":1,"b":[1,2]}'] * N) + ']')))
k.append(meas('copy_iter', lambda: [copy.copy(iter([1, 2, 3])) for i in range(N)]))
big = list(range(1000))
k.append(meas('deepcopy_iter_bigsrc', lambda: [copy.deepcopy(iter(big)) for i in range(200)], 200))
k.append(meas('str_slices_of_big', lambda: [('x' * 1000)[1:900] for i in range(2000)], 2000))
k.append(meas('memoryview?', lambda: [bytes(10) for i in range(N)]))
k.append(meas('chainmap', lambda: [collections.ChainMap() for i in range(N)]))
k.append(meas('deque_empty', lambda: [collections.deque() for i in range(N)]))
k.append(meas('defaultdict', lambda: [collections.defaultdict(list) for i in range(N)]))
k.append(meas('counter', lambda: [collections.Counter() for i in range(N)]))
k.append(meas('od', lambda: [collections.OrderedDict() for i in range(N)]))
k.append(meas('super_obj', lambda: [super(O, o) for i in range(N)]))
k.append(meas('property', lambda: [property(abs) for i in range(N)]))
k.append(meas('slice', lambda: [slice(i) for i in range(N)]))
k.append(meas('complex', lambda: [complex(i, 1) for i in range(N)]))
k.append(meas('zoneinfo', lambda: [__zi(i) for i in range(1)], 1) if False else None)
