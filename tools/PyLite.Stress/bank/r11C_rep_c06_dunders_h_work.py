# repeat: 2
# deadline: 20000
import collections, itertools, functools, heapq, bisect, operator, copy, dataclasses, enum, typing, statistics, random
def t(name, f):
    try:
        r = f()
        print(name + '=' + repr(r)[:100])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:70])

LST = []
class Mut:
    def __init__(s, v): s.v = v
    def __eq__(s, o):
        LST.clear()
        return isinstance(o, Mut) and s.v == o.v
    def __lt__(s, o):
        LST.clear()
        LST.extend(range(3))
        return s.v < o.v
    def __hash__(s):
        return hash(s.v)
    def __repr__(s):
        return 'M%d' % s.v
class Boom:
    def __eq__(s, o): raise ValueError('eq')
    def __lt__(s, o): raise ValueError('lt')
    def __hash__(s): raise ValueError('hash')
    def __repr__(s): raise ValueError('repr')
    def __len__(s): raise ValueError('len')
    def __iter__(s): raise ValueError('iter')
    def __bool__(s): raise ValueError('bool')
class BadHash:
    def __hash__(s): return 'x'
class BigHash:
    def __hash__(s): return 2 ** 100
class NegHash:
    def __hash__(s): return -1
class BigLen:
    def __len__(s): return 2 ** 63
class NegLen:
    def __len__(s): return -1
class StrLen:
    def __len__(s): return '3'
class BoolLen:
    def __len__(s): return True
class BadRepr:
    def __repr__(s): return 5
class BadStr:
    def __str__(s): return None
class BadBool:
    def __bool__(s): return 1
class BadIter:
    def __iter__(s): return 5
class SelfIter:
    def __init__(s): s.n = 0
    def __iter__(s): return s
    def __next__(s):
        s.n += 1
        if s.n > 5:
            raise StopIteration
        return s.n
class MutIterList:
    def __init__(s, l): s.l = l
    def __iter__(s):
        return (s.l.append(x) or x for x in range(5))
class GetItemOnly:
    def __getitem__(s, i):
        if i >= 3:
            raise IndexError
        return i * 10
class Contains:
    def __contains__(s, x): return 'yes'
class EqTrue:
    def __eq__(s, o): return True
    def __hash__(s): return 0
class EqNotImpl:
    def __eq__(s, o): return NotImplemented
    def __hash__(s): return 0
class LtNotImpl:
    def __lt__(s, o): return NotImplemented
class RecRepr:
    def __repr__(s): return repr([s])

t('sort_mut', lambda: (lambda l: (l.sort(), l)[1])([Mut(3), Mut(1), Mut(2)]))
def sort_self_mut():
    l = [3, 1, 2]
    def k(x):
        l.append(x)
        return x
    l.sort(key=k)
    return l
t('sort_key_mutates', sort_self_mut)
def sort_cmp_mut():
    l = [Mut(i) for i in range(50)]
    global LST
    LST = l
    l.sort()
    return len(l)
t('sort_lt_mutates', sort_cmp_mut)
t('sort_boom', lambda: sorted([Boom(), Boom()]))
t('sort_ltni', lambda: sorted([LtNotImpl(), LtNotImpl()]))
t('sort_mixed', lambda: sorted([1, 'a']))
t('sort_nan', lambda: sorted([3, float('nan'), 1, 2]))
t('sort_rev_stable', lambda: sorted([(1, 'b'), (1, 'a'), (0, 'c')], key=lambda x: x[0], reverse=True))
t('sort_key_boom', lambda: sorted([1, 2], key=lambda x: 1 / 0))
t('min_boom', lambda: min([Boom(), Boom()]))
t('min_empty_default', lambda: [min([], default=5), max((), key=len, default=None)])
t('max_gen_huge', lambda: max(range(10 ** 18, 10 ** 18 + 3)))
t('index_mut', lambda: (lambda l: l.index(Mut(2)))([Mut(1), Mut(2), Mut(3)]))
def list_eq_mut():
    global LST
    a = [Mut(1), Mut(2)]
    b = [Mut(1), Mut(2)]
    LST = a
    return a == b
t('list_eq_mut', list_eq_mut)
def list_remove_mut():
    global LST
    a = [Mut(1), Mut(2)]
    LST = a
    a.remove(Mut(2))
    return a
t('list_remove_mut', list_remove_mut)
def list_count_mut():
    global LST
    a = [Mut(1)] * 10
    LST = a
    return a.count(Mut(1))
t('list_count_mut', list_count_mut)
def list_contains_mut():
    global LST
    a = [Mut(1), Mut(2), Mut(3)]
    LST = a
    return Mut(3) in a
t('list_contains_mut', list_contains_mut)
def dict_eq_mut():
    d = {}
    class K:
        def __hash__(s): return 1
        def __eq__(s, o):
            d.clear()
            return False
    d[K()] = 1
    d[K()] = 2
    return len(d)
t('dict_eq_clears', dict_eq_mut)
def dict_eq_adds():
    d = {}
    class K:
        def __init__(s, i): s.i = i
        def __hash__(s): return 1
        def __eq__(s, o):
            if len(d) < 1000:
                d[K(len(d) + 100)] = 0
            return False
    for i in range(5):
        d[K(i)] = 1
    return len(d)
def set_eq_mut():
    s = set()
    class K:
        def __hash__(s2): return 1
        def __eq__(s2, o):
            s.clear()
            return False
    s.add(K())
    s.add(K())
    return len(s)
t('set_eq_clears', set_eq_mut)
t('dict_boom_key', lambda: {Boom(): 1})
t('dict_badhash', lambda: {BadHash(): 1})
t('dict_bighash', lambda: {BigHash(): 1, NegHash(): 2})
t('hash_big', lambda: [hash(BigHash()), hash(NegHash())])
t('len_big', lambda: len(BigLen()))
t('len_neg', lambda: len(NegLen()))
t('len_str', lambda: len(StrLen()))
t('len_bool', lambda: len(BoolLen()))
t('bool_biglen', lambda: bool(BigLen()))
t('bool_neglen', lambda: bool(NegLen()))
t('bool_bad', lambda: bool(BadBool()))
t('if_bad_bool', lambda: 1 if BadBool() else 0)
t('repr_bad', lambda: repr(BadRepr()))
t('repr_bad_in_list', lambda: repr([BadRepr()]))
t('str_bad', lambda: str(BadStr()))
t('fstr_bad', lambda: f'{BadStr()}')
t('iter_bad', lambda: list(BadIter()))
t('iter_self', lambda: list(SelfIter()))
t('iter_getitem', lambda: [list(GetItemOnly()), 20 in GetItemOnly(), sorted(GetItemOnly(), reverse=True)])
t('contains_truthy', lambda: [1 in Contains(), 1 not in Contains()])
t('eq_true', lambda: [EqTrue() in [1], [1].count(EqTrue()), [1, 2].index(EqTrue()), EqTrue() == None, {EqTrue(): 1}.get(0)])
t('eq_notimpl', lambda: [EqNotImpl() == EqNotImpl(), EqNotImpl() in [EqNotImpl()], EqNotImpl() != 1])
t('list_extend_self_iter', lambda: (lambda l: (l.extend(MutIterList(l)), len(l)))([1]))
t('list_slice_assign_self', lambda: (lambda l: (l.__setitem__(slice(1, 2), l), l))([1, 2, 3]))
t('list_slice_assign_self2', lambda: (lambda l: (l.__setitem__(slice(None, None, -1), l), l))([1, 2, 3]))
t('list_ext_step', lambda: (lambda l: (l.__setitem__(slice(None, None, 2), [9]), l))([1, 2, 3]))
t('list_del_step', lambda: (lambda l: (l.__delitem__(slice(None, None, -2)), l))(list(range(7))))
t('list_insert_big', lambda: (lambda l: (l.insert(2 ** 100, 9), l.insert(-2 ** 100, 8), l))([1]))
t('list_pop_big', lambda: [1].pop(2 ** 100))
t('list_mul_inplace_self', lambda: (lambda l: (l.__imul__(3), l.__iadd__(l), len(l)))([1, 2]))
t('list_imul_huge', lambda: (lambda l: l.__imul__(2 ** 62))([1, 2]))
t('tuple_hash_boom', lambda: hash((1, Boom())))
t('tuple_cmp_boom', lambda: (1, Boom()) < (1, Boom()))
t('frozenset_boom', lambda: frozenset([1, Boom()]))
t('set_ops_mixed', lambda: [{1, 2} | frozenset([3]), frozenset([1]) & {1, 2}, {1} ^ {1}, {1} <= {1, 2}, {1}.isdisjoint([2, Boom()]) if False else 0])
t('set_isdisjoint_boom', lambda: {1}.isdisjoint([2, Boom()]))
t('counter_ops', lambda: [collections.Counter('aab') - collections.Counter('abc'), collections.Counter(a=-1) + collections.Counter(), collections.Counter(a=3) & collections.Counter(a=-1), +collections.Counter(a=-2, b=0, c=1), -collections.Counter(a=-2), collections.Counter('ab') <= collections.Counter('abc')])
t('counter_mostcommon', lambda: [collections.Counter('abcabcab').most_common(2 ** 63), collections.Counter('ab').most_common(-1), collections.Counter('ab').most_common(0)])
t('counter_total', lambda: [collections.Counter(a=10 ** 30, b=-1).total(), collections.Counter(a=1.5).total()])
t('counter_elements', lambda: list(collections.Counter(a=2, b=0, c=-1, d=1.0).elements()))
t('counter_elements_huge', lambda: len(list(itertools.islice(collections.Counter(a=10 ** 20).elements(), 5))))
t('counter_update_boom', lambda: collections.Counter().update([1, Boom()]))
t('counter_mixed_vals', lambda: collections.Counter(a='x', b=1).most_common())
t('counter_repr_mixed', lambda: repr(collections.Counter(a='x', b=1)))
t('counter_fromkeys', lambda: collections.Counter.fromkeys('ab'))
t('counter_eq', lambda: [collections.Counter(a=1, b=0) == collections.Counter(a=1), collections.Counter(a=1) == {'a': 1}])
t('counter_subtract', lambda: (lambda c: (c.subtract('aaz'), c.subtract(a=Boom()) if False else 0, c)[2])(collections.Counter('ab')))
t('counter_lt', lambda: [collections.Counter('a') < collections.Counter('ab'), collections.Counter('ab') > collections.Counter('ab')])
t('deque_ops', lambda: (lambda d: (d.rotate(2 ** 70), d.rotate(-2 ** 70 - 1), d.extendleft('xy'), d.insert(100, 'z'), d.index('a', -100, 100), list(d)))(collections.deque('abc', 5)))
t('deque_insert_full', lambda: collections.deque('ab', 2).insert(0, 'z'))
t('deque_maxlen0', lambda: (lambda d: (d.append(1), d.extend(range(10 ** 6)), list(d)))(collections.deque(maxlen=0)))
t('deque_mul', lambda: [collections.deque('ab', 3) * 5, collections.deque('ab') * -1])
t('deque_mul_huge', lambda: len(collections.deque('ab', 3) * (2 ** 62)))
t('deque_mut_iter', lambda: (lambda d: [d.append(x) for x in d])(collections.deque([1])))
t('deque_eq_mut', lambda: (lambda d: d == collections.deque([Mut(1)]))(collections.deque([Mut(1)])))
t('deque_remove_mut', lambda: (lambda d: (d.remove(Mut(5)) if False else d.count(Mut(1))))(collections.deque([Mut(1)] * 3)))
t('deque_getitem', lambda: collections.deque('abc')[-4])
t('deque_maxlen_bad', lambda: collections.deque([], -1))
t('deque_maxlen_huge', lambda: collections.deque([1], 2 ** 70))
t('od_ops', lambda: (lambda o: (o.move_to_end('a'), o.move_to_end('c', last=False), o.popitem(last=False), list(o)))(collections.OrderedDict.fromkeys('abc')))
t('od_move_missing', lambda: collections.OrderedDict().move_to_end('x'))
t('od_eq', lambda: [collections.OrderedDict(a=1, b=2) == collections.OrderedDict(b=2, a=1), collections.OrderedDict(a=1, b=2) == dict(b=2, a=1)])
t('od_mut_iter', lambda: (lambda o: [o.__setitem__(k + 'x', 1) for k in o])(collections.OrderedDict(a=1)))
t('dd_factory', lambda: (lambda d: (d['x'].append(1), d['y'], dict(d)))(collections.defaultdict(list)))
t('dd_factory_boom', lambda: collections.defaultdict(lambda: 1 / 0)['x'])
t('dd_factory_bad', lambda: collections.defaultdict(5))
t('dd_missing_get', lambda: (lambda d: (d.get('x'), 'x' in d, len(d)))(collections.defaultdict(int)))
t('chainmap', lambda: (lambda c: (c['a'], c.new_child({'a': 9})['a'], c.parents.maps, list(c), len(c)))(collections.ChainMap({'a': 1}, {'a': 2, 'b': 3})))
t('chainmap_del_missing', lambda: collections.ChainMap({}, {'a': 1}).__delitem__('a'))
t('chainmap_self', lambda: (lambda m: (m.maps.append(m), m['zz'] if False else len(m.maps)))(collections.ChainMap({})))
t('nt', lambda: (lambda P: (P(1, 2)._replace(x=5), P._make([1, 2]), P(1, 2)._asdict(), P._fields, P(1, y=2) == (1, 2)))(collections.namedtuple('P', 'x y')))
t('nt_bad', lambda: collections.namedtuple('P', 'x x'))
t('nt_bad2', lambda: collections.namedtuple('P', ['class', '_a']))
t('nt_rename', lambda: collections.namedtuple('P', ['class', '_a', 'x', 'x'], rename=True)._fields)
t('nt_defaults', lambda: collections.namedtuple('P', 'x y', defaults=[1, 2, 3]))
t('nt_replace_bad', lambda: collections.namedtuple('P', 'x')(1)._replace(z=1))
t('nt_many', lambda: len(collections.namedtuple('P', ['f%d' % i for i in range(3000)])._fields))
t('it_product_huge', lambda: list(itertools.islice(itertools.product(range(10 ** 6), repeat=10 ** 6), 1)))
t('it_product_rep0', lambda: list(itertools.product(range(3), repeat=0)))
t('it_comb', lambda: [len(list(itertools.combinations(range(30), 2))), list(itertools.combinations([], 0)), list(itertools.combinations([1], 2)), list(itertools.permutations([1, 2], 0))])
t('it_comb_huge', lambda: list(itertools.islice(itertools.combinations(range(10 ** 6), 10 ** 5), 1))[0][:3])
t('it_perm_huge', lambda: list(itertools.islice(itertools.permutations(range(10 ** 6)), 1))[0][:3])
t('it_cwr_huge', lambda: list(itertools.islice(itertools.combinations_with_replacement(range(3), 10 ** 6), 1))[0][:3])
t('it_count', lambda: [list(itertools.islice(itertools.count(2 ** 64, -0.5), 3)), list(itertools.islice(itertools.count(1.5), 2))])
t('it_repeat', lambda: [list(itertools.repeat(1, -5)), list(itertools.islice(itertools.repeat(1), 3))])
t('it_islice', lambda: [list(itertools.islice(range(10), 2 ** 70)), list(itertools.islice(range(10), 2, None, 3))])
t('it_islice_neg', lambda: itertools.islice(range(3), -1))
t('it_islice_step0', lambda: itertools.islice(range(3), 0, 3, 0))
t('it_tee', lambda: (lambda a: (list(a[0]), list(a[1])))(itertools.tee(iter([1, 2]), 2)))
t('it_tee_neg', lambda: itertools.tee([1], -1))
t('it_groupby', lambda: [(k, list(g)) for k, g in itertools.groupby('aabbbc')])
t('it_groupby_stale', lambda: (lambda gs: [list(g) for g in gs])([g for k, g in itertools.groupby('aabb')]))
t('it_groupby_boom', lambda: list(itertools.groupby([Boom(), Boom()])))
t('it_accumulate', lambda: [list(itertools.accumulate([1, 2, 3], initial=10)), list(itertools.accumulate([], initial=None)), list(itertools.accumulate('abc'))])
t('it_batched', lambda: [list(itertools.batched('abcde', 2)), list(itertools.batched('abc', 2, strict=False))])
t('it_batched_strict', lambda: list(itertools.batched('abc', 2, strict=True)))
t('it_batched_bad', lambda: itertools.batched('a', 0))
t('it_batched_huge', lambda: list(itertools.batched('ab', 2 ** 62)))
t('it_pairwise', lambda: list(itertools.pairwise('abc')))
t('it_zip_longest', lambda: list(itertools.zip_longest('ab', 'c', fillvalue=0)))
t('it_zip_strict', lambda: list(zip('ab', 'c', strict=True)))
t('it_chain_self', lambda: (lambda l: (l.extend(itertools.chain(l)) if False else list(itertools.islice(itertools.chain.from_iterable(itertools.repeat([1])), 3))))([1]))
t('it_starmap', lambda: list(itertools.starmap(pow, [(2, 3), (2, -1, 5)])))
t('it_cycle', lambda: list(itertools.islice(itertools.cycle('ab'), 5)))
t('it_cycle_empty', lambda: list(itertools.cycle([])))
t('it_compress', lambda: list(itertools.compress('abc', [1, 0, Boom()])))
t('ft_reduce', lambda: [functools.reduce(operator.add, [], 0), functools.reduce(operator.add, [1])])
t('ft_reduce_empty', lambda: functools.reduce(operator.add, []))
t('ft_partial', lambda: [functools.partial(functools.partial(int, base=2), '101')(), repr(functools.partial(max, 1, key=None))])
t('ft_lru', lambda: (lambda f: (f(1), f(1), f(Boom()) if False else 0, f.cache_info()))(functools.lru_cache(maxsize=2)(lambda x: x)))
t('ft_lru_boom', lambda: functools.lru_cache()(lambda x: x)(Boom()))
t('ft_lru_neg', lambda: (lambda f: (f(1), f(1), f.cache_info()))(functools.lru_cache(maxsize=-1)(lambda x: x)))
t('ft_lru_kw', lambda: (lambda f: (f(1, a=2), f(1, a=2), f.cache_info().hits))(functools.lru_cache(typed=True)(lambda x, a: x)))
def lru_rec():
    @functools.lru_cache(maxsize=None)
    def fib(n):
        return n if n < 2 else fib(n - 1) + fib(n - 2)
    return fib(150)
def lru_reentrant_clear():
    @functools.lru_cache(maxsize=3)
    def f(n):
        f.cache_clear()
        return n if n < 1 else f(n - 1) + 1
    return [f(50), f.cache_info()]
t('ft_lru_reentrant_clear', lru_reentrant_clear)
t('ft_cache', lambda: functools.cache(lambda x: [x])(1))
t('ft_cmp_to_key', lambda: sorted([3, 1, 2], key=functools.cmp_to_key(lambda a, b: 'x')))
t('ft_total_ordering', lambda: functools.total_ordering)
t('heapq', lambda: (lambda h: (heapq.heapify(h), heapq.heappushpop(h, 0), heapq.nsmallest(2 ** 63, h), heapq.nlargest(-1, h), heapq.merge([1, 3], [2], reverse=False) and list(heapq.merge([1, 3], [2]))))([3, 1, 2]))
t('heapq_mut', lambda: (lambda h: (heapq.heapify(h), [heapq.heappush(h, Mut(i)) for i in range(20)], len(h)))([Mut(3), Mut(1)]))
t('heapq_boom', lambda: heapq.heapify([Boom(), Boom()]))
t('heapq_max', lambda: [getattr(heapq, n, None) is not None for n in ('heapify_max', 'heappush_max', 'heappop_max')])
t('bisect', lambda: [bisect.bisect([1, 2, 3], 2, -5, 10 ** 30) if False else bisect.bisect([1, 2, 3], 2), bisect.bisect_left([1, 2], 2, key=lambda x: x)])
t('bisect_hi_huge', lambda: bisect.bisect([1, 2, 3], 2, 0, 10 ** 30))
t('bisect_lo_neg', lambda: bisect.bisect([1, 2, 3], 2, -1))
t('bisect_mut', lambda: (lambda l: bisect.insort(l, Mut(2)) or len(l))([Mut(1), Mut(3)] * 20))
t('copy_rec', lambda: (lambda l: (l.append(l), copy.deepcopy(l)[1] is not l))([1]))
t('copy_dict_rec', lambda: (lambda d: (d.__setitem__('s', d) if False else d.update(s=d), copy.deepcopy(d)['s']['s'] is not d))({}))
t('copy_obj', lambda: (lambda m: copy.deepcopy(m).v)(Mut(5)))
t('dc', lambda: (lambda D: (D(1), D(1) == D(1), dataclasses.astuple(D([1, [2]])), dataclasses.replace(D(1), x=2), dataclasses.fields(D)[0].name))(dataclasses.dataclass(type('D', (), {'__annotations__': {'x': int}}))))
@dataclasses.dataclass(order=True, frozen=True)
class P:
    x: int
    y: list = dataclasses.field(default_factory=list, compare=False)
t('dc_frozen', lambda: (lambda p: p.__setattr__('x', 2))(P(1)))
t('dc_order', lambda: [P(1) < P(2), sorted([P(2), P(1)]), hash(P(1)) == hash(P(1))])
t('dc_hash_list', lambda: hash(P(1, [1])))
t('dc_asdict_rec', lambda: (lambda p: (p.y.append(p), dataclasses.asdict(p)))(P(1)))
t('dc_eq_rec', lambda: (lambda p, q: (p.y.append(p), q.y.append(q), p == q))(P(1), P(1)))
t('dc_repr_rec', lambda: (lambda p: (p.y.append(p), repr(p)))(P(1)))
class Color(enum.Enum):
    R = 1
    G = 2
    B = 1
t('enum', lambda: [Color(1), Color.B is Color.R, list(Color), Color['G']])
t('enum_bad', lambda: Color(5))
t('enum_bad2', lambda: Color['Z'])
t('typing', lambda: [typing.List[int], typing.Optional[typing.Union[int, str]], typing.Dict[str, typing.List[int]], typing.Callable[[int], str], typing.Literal[1, 'a'], typing.Tuple[()], typing.get_args(typing.Union[int, str]), typing.get_origin(typing.List[int])])
t('typing_deep', lambda: len(repr(functools.reduce(lambda a, _: typing.List[a], range(5000), int))))
t('typing_union_many', lambda: len(typing.get_args(typing.Union[tuple(typing.Literal[i] for i in range(3000))])))
t('typing_union_dup', lambda: typing.Union[int, int, typing.Union[int, str]])
t('typing_literal_unhashable', lambda: typing.Literal[[1]])
t('typing_literal_eq', lambda: [typing.Literal[1] == typing.Literal[True], typing.Literal[1, 2] == typing.Literal[2, 1], hash(typing.Literal[1]) is not None])
t('typing_optional_none', lambda: [typing.Optional[None], typing.Union[None]])
t('typing_union_bad', lambda: typing.Union[1])
t('typing_cast', lambda: [typing.cast(int, 'x'), typing.TYPE_CHECKING, typing.NamedTuple, typing.TypedDict])
t('typing_nt', lambda: typing.NamedTuple('P', [('x', int)])(1))
t('typing_newtype', lambda: typing.NewType('U', int)(5))
t('typing_typevar', lambda: [typing.TypeVar('T'), typing.TypeVar('T', int, str).__constraints__, typing.Generic])
t('typing_annotated', lambda: typing.Annotated[int, 'x'])
t('typing_getitem_tuple', lambda: typing.Dict[(int, str)])
t('typing_callable_bad', lambda: typing.Callable[int, str])
t('typing_isinstance', lambda: isinstance(1, typing.List[int]))
t('typing_isinstance_union', lambda: [isinstance(1, typing.Union[int, str]), isinstance(1, int | str), isinstance(None, typing.Optional[int])])
t('stat', lambda: [statistics.median([]), 0] if False else [statistics.mean([1, 2]), statistics.fmean([1e308, 1e308]), statistics.median_low([3, 1]), statistics.mode('aab'), statistics.multimode(''), statistics.quantiles([1, 2, 3, 4], n=4)])
t('stat_empty', lambda: statistics.mean([]))
t('stat_nan', lambda: [statistics.median([float('nan'), 1, 2]), statistics.mean([float('inf'), float('-inf')])])
t('stat_huge', lambda: statistics.mean([10 ** 400, 1]) > 0)
t('stat_var_one', lambda: statistics.variance([1]))
t('stat_quantiles_bad', lambda: statistics.quantiles([1], n=0))
t('stat_corr', lambda: statistics.correlation([1, 2, 3], [1, 1, 1]))
t('rand', lambda: (lambda r: (r.randrange(10 ** 30) >= 0, r.getrandbits(0), r.choices([1, 2], k=0), r.sample(range(10 ** 18), 2)[0] >= 0, r.randint(5, 5)))(random.Random(1)))
t('rand_bad', lambda: random.Random(1).randrange(5, 5))
t('rand_bits_huge', lambda: random.Random(1).getrandbits(10 ** 7))
t('rand_sample_huge', lambda: random.Random(1).sample(range(10), 11))
t('rand_choices_huge', lambda: len(random.Random(1).choices([1], k=10 ** 7)))
t('rand_seed_str', lambda: [random.Random('abc').random(), random.Random(b'abc').random(), random.Random(10 ** 100).random(), random.Random(-5).random() == random.Random(5).random()])
t('rand_seed_bad', lambda: random.Random([1]))
t('rand_shuffle_mut', lambda: (lambda l: (random.Random(1).shuffle(l), len(l)))(list(range(10))))
t('rand_binomial', lambda: random.Random(1).binomialvariate(10 ** 18, 0.5) > 0)
t('op_getters', lambda: [operator.itemgetter(0, slice(1, None))('abc'), operator.attrgetter('real.imag')(5), operator.methodcaller('split', ',', 1)('a,b,c')])
t('op_index', lambda: [operator.index(True), operator.length_hint([1, 2]), operator.length_hint(iter(range(5))), operator.length_hint(BigLen(), 3) if False else 0])
t('op_length_hint_big', lambda: operator.length_hint(BigLen()))
t('op_concat_bad', lambda: operator.concat(1, 2))
