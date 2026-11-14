from collections import Counter, deque, ChainMap, OrderedDict, defaultdict
import collections, itertools
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:200])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e).replace("__main__.", "").replace(chr(39) + "collections.ChainMap", chr(39) + "ChainMap")[:200])
def rv(mut):
    d = deque(range(5)); it = reversed(d); next(it)
    mut(d)
    return list(it)
def m_set(d): d[0] = 7
def m_del(d): del d[0]
def m_iadd(d): d += [1]
def m_imul(d): d *= 2
def m_imul1(d): d *= 1
def m_iadd0(d): d += []
def m_extl(d): d.extendleft([])
def m_extl1(d): d.extendleft([1])
def m_copy(d): d.copy()
def m_popleft(d): d.popleft()
def m_same(d): d.append(1); d.pop()
for nm, f in (('set', m_set), ('del', m_del), ('iadd', m_iadd), ('imul', m_imul), ('imul1', m_imul1), ('iadd0', m_iadd0),
              ('extl', m_extl), ('extl1', m_extl1), ('copy', m_copy), ('popleft', m_popleft), ('same', m_same)):
    t('rv_' + nm, lambda: rv(f))
def fwd(mut):
    d = deque(range(5)); it = iter(d); next(it)
    mut(d)
    return list(it)
for nm, f in (('set', m_set), ('del', m_del), ('iadd', m_iadd), ('imul1', m_imul1), ('iadd0', m_iadd0), ('same', m_same)):
    t('fw_' + nm, lambda: fwd(f))
t('rv_empty', lambda: list(reversed(deque())))
t('rv_full', lambda: list(reversed(deque('abc'))))
t('rv_maxlen', lambda: list(reversed(deque(range(10), maxlen=3))))
t('rv_rot', lambda: [d := deque(range(5)), d.rotate(2), list(reversed(d))][2])
t('rv_two', lambda: [d := deque(range(3)), a := reversed(d), b := reversed(d), next(a), next(b), next(a), list(b)][3:])
# ChainMap
def kw(**k): return sorted(k.items())
t('ss_cm', lambda: kw(**ChainMap({'a': 1}, {'b': 2, 'a': 9})))
t('ss_cm_dup', lambda: kw(a=1, **ChainMap({'a': 1})))
t('ss_cm_nonstr', lambda: kw(**ChainMap({1: 1})))
class M:
    def __init__(self, ks): self.ks = ks
    def keys(self): return self.ks
    def __getitem__(self, k): return k * 2
t('ss_obj', lambda: kw(**M(['a', 'b'])))
t('ss_obj_dup', lambda: kw(**M(['a', 'a'])))
t('ss_obj_nonstr', lambda: kw(**M([1])))
class N:
    def keys(self): return ['a']
t('ss_obj_noget', lambda: kw(**N()))
t('ss_list', lambda: kw(**[1]))
t('ss_none', lambda: kw(**None))
t('ss_od', lambda: kw(**OrderedDict(a=1)))
t('ss_counter', lambda: kw(**Counter(a=1)))
t('ss_two', lambda: kw(**ChainMap({'a': 1}), **{'b': 2}))
t('ss_two_dup', lambda: kw(**ChainMap({'a': 1}), **{'a': 2}))
t('cm_or_counter_first', lambda: (ChainMap(Counter(a=1), {'z': 0}) | {'a': 2, 'b': 3}).maps)
def ior(c, o):
    d = c
    c |= o
    return c.maps, c is d
t('cm_ior_counter_first', lambda: ior(ChainMap(Counter(a=1)), {'a': 2}))
t('cm_ior_pairs', lambda: ior(ChainMap({'a': 1}), [('b', 2)]))
t('cm_ior_cm', lambda: ior(ChainMap({'a': 1}), ChainMap({'b': 2}, {'c': 3})))
t('cm_ior_int', lambda: ior(ChainMap({'a': 1}), 5))
t('cm_ior_dd', lambda: ior(ChainMap(defaultdict(list)), {'a': 2}))
t('cm_or_dd', lambda: [type(m).__name__ for m in (ChainMap(defaultdict(list), OrderedDict()) | {'a': 2}).maps])
t('cm_or_empty_maps', lambda: [c := ChainMap({'a': 1}), c.maps.clear(), c | {'b': 1}][2])
t('cm_ior_empty_maps', lambda: [c := ChainMap({'a': 1}), c.maps.clear(), ior(c, {'b': 1})][2])
t('cm_ror_empty_maps', lambda: [c := ChainMap({'a': 1}), c.maps.clear(), ({'b': 1} | c).maps][2])
t('cm_or_self', lambda: [c := ChainMap({'a': 1}, {'b': 2}), (c | c).maps][1])
t('cm_and', lambda: ChainMap({'a': 1}) & {'a': 1})
t('cm_add', lambda: ChainMap({'a': 1}) + {'a': 1})
t('dict_upd_cm', lambda: [d := {'x': 0}, d.update(ChainMap({'a': 1})), d][2])
def dior():
    d = {'x': 0}
    d |= ChainMap({'a': 1}, {'b': 2})
    return d
t('dict_ior_cm2', dior)
# Counter
t('c_upd_order', lambda: [c := Counter(a=1), c.update({'a': 2.5, 'b': 1}), c][2])
t('c_upd_self', lambda: [c := Counter(a=1, b=2), c.update(c), c][2])
t('c_upd_self_empty', lambda: [c := Counter(), c.update(c), c][2])
t('c_upd_iter_str', lambda: [c := Counter({'a': 'x'}), c.update('a'), c][2])
t('c_sub_iter_str', lambda: [c := Counter({'a': 'x'}), c.subtract('a'), c][2])
t('c_sub_empty', lambda: [c := Counter(), c.subtract({'a': 2}), c][2])
t('c_sub_kw', lambda: [c := Counter(), c.subtract(a=2), c][2])
t('c_sub_kw_str', lambda: [c := Counter(), c.subtract(a='x'), c][2])
t('c_bool', lambda: Counter({'a': True, 'b': 2}))
t('c_bool_kw', lambda: Counter(a=True))
t('c_from_cm', lambda: Counter(ChainMap({'a': 2}, {'b': 3, 'a': 9})))
t('c_from_cm_nonempty', lambda: [c := Counter(a=1), c.update(ChainMap({'a': 2}, {'b': 3})), c][2])
t('c_from_od', lambda: Counter(OrderedDict(b=1, a=2)))
t('c_from_dd', lambda: Counter(defaultdict(int, a=2)))
t('c_from_pairs', lambda: Counter([('a', 1), ('a', 1)]))
t('c_kw_after_iter', lambda: Counter('ab', a=5))
t('c_kw_after_empty_iter', lambda: Counter('', a='x'))
t('c_kw_after_empty_map', lambda: Counter({}, a='x'))
t('c_none', lambda: Counter(None, a=3))
t('c_mc', lambda: Counter('abracadabra').most_common())
t('c_mc2', lambda: Counter('abracadabra').most_common(2))
t('c_mc0', lambda: Counter('abracadabra').most_common(0))
t('c_mc_neg', lambda: Counter('abracadabra').most_common(-1))
t('c_mc_float', lambda: Counter({'a': 1.5, 'b': 2, 'c': 1.5, 'd': True}).most_common())
t('c_mc_big', lambda: Counter({'a': 10**30, 'b': 2, 'c': 10**30, 'd': -5}).most_common())
t('c_mc_mixed', lambda: Counter({'a': 1, 'b': 'x'}).most_common())
t('c_mc_none', lambda: Counter({'a': None, 'b': None}).most_common())
t('c_mc_one_none', lambda: Counter({'a': None}).most_common())
t('c_mc_str_n', lambda: Counter({'a': 'x', 'b': 'y', 'c': 'x'}).most_common(2))
t('c_mc_tuple', lambda: Counter({'a': (1, 2), 'b': (1, 3), 'c': (0,)}).most_common())
t('c_mc_nan', lambda: [k for k, v in Counter({'a': 1.0, 'b': float('nan'), 'c': 3.0, 'd': 2.0}).most_common()])
t('c_repr_ties', lambda: repr(Counter({'b': 1, 'a': 1, 'c': 2})))
t('c_repr_str', lambda: repr(Counter({'a': 'x', 'b': 'y', 'c': 'x'})))
t('c_repr_float', lambda: repr(Counter({'a': 1.5, 'b': 2.5, 'c': 1.5})))
t('c_repr_big', lambda: repr(Counter({'a': 1, 'b': 10**30})))
t('c_el_mixed', lambda: list(Counter({'a': 2, 'b': 'x', 'c': 1}).elements()))
def el_resume():
    it = Counter({'a': 1, 'b': 'x', 'c': 2}).elements()
    r = [next(it)]
    try:
        next(it)
    except TypeError as e:
        r.append('TE')
    r.extend(it)
    return r
t('c_el_resume', el_resume)
t('c_el_neg_big', lambda: list(Counter({'a': -2**31}).elements()))
t('c_el_huge', lambda: next(Counter({'a': 2**64}).elements()))
t('c_el_neg_huge', lambda: list(Counter({'a': -2**64}).elements()))
t('c_add_upd', lambda: Counter(a=1) + Counter({'a': 2.5}))
t('c_total_float', lambda: Counter({'a': 1.5, 'b': 2}).total())
t('c_copy_type', lambda: type(Counter({'a': 'x'}).copy()).__name__)
t('c_eq_dict', lambda: Counter({'a': 'x'}) == {'a': 'x'})
print(chr(10).join(out))
