import xml.etree.ElementTree as ET, copy, gzip, itertools, collections
from collections import Counter, deque, ChainMap, OrderedDict, defaultdict
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:200])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e).replace(chr(39) + "collections.ChainMap", chr(39) + "ChainMap")[:200])
# A-2
def a2(kind, cp):
    e = ET.Element('a')
    e.attrib = kind()
    c = cp(e)
    return type(c.attrib).__name__, c.attrib is e.attrib
for kn, k in (('dict', dict), ('od', OrderedDict), ('dd', lambda: defaultdict(list))):
    t('a2_copy_' + kn, lambda: a2(k, copy.copy))
    t('a2_deep_' + kn, lambda: a2(k, copy.deepcopy))
# C-9
t('el_none', lambda: ET.Element('a', None).attrib)
t('el_none_kw', lambda: ET.Element('a', attrib=None).attrib)
t('el_list', lambda: ET.Element('a', []).attrib)
t('el_od', lambda: type(ET.Element('a', OrderedDict(x='1')).attrib).__name__)
t('sub_none', lambda: ET.SubElement(ET.Element('r'), 'a', None).attrib)
t('sub_list', lambda: ET.SubElement(ET.Element('r'), 'a', [1]).attrib)
t('mk_none', lambda: ET.Element('r').makeelement('a', None).attrib)
t('el_kw', lambda: ET.Element('a', {'x': '1'}, y='2').attrib)
# C-6 prefix
r = ET.fromstring('<r xmlns:p="u"><p:b/><b/><c><p:b/></c></r>')
for path in ('p:b', './p:b', 'c/p:b', './/p:b', 'q:b', 'p:b[1]', '{u}b', 'b', ':b', 'p:', 'x/q:z'):
    for nm, ns in (('none', None), ('empty', {}), ('p', {'p': 'u'}), ('other', {'z': 'u'})):
        t('find %s %s' % (path, nm), lambda: getattr(r.find(path, ns), 'tag', None))
    t('findall %s' % path, lambda: len(r.findall(path)))
    t('findtext %s' % path, lambda: r.findtext(path, 'dflt'))
    t('iterfind %s' % path, lambda: len(list(r.iterfind(path))))
t('iterfind lazy', lambda: type(r.iterfind('q:b')).__name__)
# C-8 gzip
for m in (0, 1, -1, 2**32 - 1, 2**32, 2**40, -2**31, 1.5, 'x', True, 2**64):
    t('gz mtime %r' % (m,), lambda: gzip.compress(b'a', mtime=m)[4:8].hex())
t('gz lvl', lambda: gzip.compress(b'a', 9, mtime=5)[4:8].hex())
# C-7 / C-8
for n in (2**64, -1, 0, 1.0, 'x'):
    t('batched %r' % (n,), lambda: next(itertools.batched([1, 2, 3], n)))
    t('deque maxlen %r' % (n,), lambda: deque([1, 2], maxlen=n).maxlen)
    t('rotate %r' % (n,), lambda: [d := deque([1, 2, 3]), d.rotate(n), list(d)][2])
    t('rotate -%r' % (n,), lambda: [d := deque([1, 2, 3]), d.rotate(-n) if not isinstance(n, str) else d.rotate(n), list(d)][2])
for v in (2.0, 2.5, -1.0, 0.0, True, 3, '2', None, [1], 1e3):
    t('elements %r' % (v,), lambda: len(list(itertools.islice(Counter({'a': v}).elements(), 5000))))
# C-4 Counter
t('kw_str', lambda: Counter(a='x'))
t('kw_none', lambda: Counter(a=None))
t('kw_mixed', lambda: Counter(a=1, b='x'))
t('map_str', lambda: Counter({'a': 'x'}))
t('map_mixed', lambda: Counter({'a': 1, 'b': 'x'}))
t('map_list_vals', lambda: Counter({'a': [1]}))
t('map_none', lambda: Counter({'a': None}))
t('map_then_kw', lambda: Counter({'a': 1}, a='x'))
t('map_then_kw2', lambda: Counter({'a': 'y'}, a='x'))
t('update_nonempty_str', lambda: [c := Counter(a=1), c.update({'b': 'x'}), c][2])
t('update_nonempty_str2', lambda: [c := Counter(a=1), c.update({'a': 'x'}), c][2])
t('update_empty_str', lambda: [c := Counter(), c.update({'b': 'x'}), c][2])
t('update_kw_str', lambda: [c := Counter(), c.update(b='x'), c][2])
t('sub_str', lambda: [c := Counter(a=1), c.subtract({'a': 'x'}), c][2])
t('str_most_common', lambda: Counter({'a': 'x', 'b': 'y'}).most_common())
t('str_total', lambda: Counter({'a': 'x', 'b': 'y'}).total())
t('str_total2', lambda: Counter({'a': 'x'}).total())
t('str_elements', lambda: list(Counter({'a': 'x'}).elements()))
t('str_add', lambda: Counter({'a': 'x'}) + Counter())
t('str_pos', lambda: +Counter({'a': 'x'}))
t('str_eq', lambda: Counter({'a': 'x'}) == Counter({'a': 'x'}))
t('str_getitem', lambda: Counter({'a': 'x'})['a'])
t('str_copy', lambda: Counter({'a': 'x'}).copy())
t('str_from_counter', lambda: Counter(Counter({'a': 'x'})))
t('none_repr', lambda: repr(Counter({'a': None, 'b': None})))
t('mixed_repr', lambda: repr(Counter({'a': 1, 'b': 'x'})))
t('list_repr', lambda: repr(Counter({'a': [1], 'b': [0]})))
t('fromdict_float', lambda: Counter({'a': 1.5, 'b': 2}))
t('iter_str', lambda: Counter('abca'))
t('iter_pairs', lambda: Counter([('a', 'x')]))
# C-6 ChainMap |
t('cm_or', lambda: ChainMap({'a': 1}) | {'b': 2})
t('cm_or_type', lambda: type(ChainMap({'a': 1}) | {'b': 2}).__name__)
t('cm_or_maps', lambda: (ChainMap({'a': 1}, {'c': 3}) | {'b': 2, 'c': 9}).maps)
t('cm_ror', lambda: {'b': 2} | ChainMap({'a': 1}))
t('cm_ror_type', lambda: type({'b': 2} | ChainMap({'a': 1})).__name__)
t('cm_ror_maps', lambda: ({'b': 2, 'c': 0} | ChainMap({'a': 1}, {'c': 3, 'a': 7})).maps)
def ior2():
    c = ChainMap({'a': 1}, {'z': 0})
    d = c
    c |= {'b': 2}
    return c.maps, c is d
t('cm_ior2', ior2)
t('cm_or_cm', lambda: (ChainMap({'a': 1}, {'c': 3}) | ChainMap({'b': 2}, {'c': 5})).maps)
t('cm_or_list', lambda: ChainMap({'a': 1}) | [('b', 2)])
t('cm_or_int', lambda: ChainMap({'a': 1}) | 5)
t('cm_ror_list', lambda: [('b', 2)] | ChainMap({'a': 1}))
t('cm_ror_od', lambda: (OrderedDict(b=2) | ChainMap({'a': 1})).maps)
t('cm_or_od', lambda: (ChainMap({'a': 1}) | OrderedDict(b=2)).maps)
t('cm_or_counter', lambda: (ChainMap({'a': 1}) | Counter(b=2)).maps)
t('cm_ror_counter', lambda: type(Counter(b=2) | ChainMap({'a': 1})).__name__)
t('cm_or_fresh', lambda: [m := {'a': 1}, c := ChainMap(m) | {'b': 2}, c.maps[0] is m, m][2:])
# C-7 reversed deque
t('reversed_mut', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.pop(), next(it)][4])
t('reversed_mut_append', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.append(9), next(it)][4])
t('reversed_mut_rot', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.rotate(1), next(it)][4])
t('reversed_mut_rot0', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.rotate(0), next(it)][4])
t('reversed_mut_rot5', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.rotate(5), next(it)][4])
t('reversed_mut_clear', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.clear(), next(it)][4])
t('reversed_mut_poppush', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.pop(), d.append(1), next(it)][5])
t('reversed_mut_ext0', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.extend([]), next(it)][4])
t('reversed_mut_rev', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.reverse(), next(it)][4])
t('reversed_mut_done', lambda: [d := deque(range(2)), it := reversed(d), list(it), d.append(1), list(it)][4])
t('reversed_mut_appendleft', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.appendleft(9), next(it)][4])
t('reversed_mut_remove', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.remove(0), next(it)][4])
t('reversed_mut_insert', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.insert(0, 4), next(it)][4])
t('reversed_mut_maxlen', lambda: [d := deque(range(5), maxlen=5), it := reversed(d), next(it), d.append(1), next(it)][4])
t('iter_mut', lambda: [d := deque(range(5)), it := iter(d), next(it), d.pop(), next(it)][4])
t('reversed_list_mut', lambda: [d := list(range(5)), it := reversed(d), next(it), d.pop(), next(it)][4])
t('reversed_mut_then_stop', lambda: [d := deque(range(5)), it := reversed(d), next(it), d.pop(), [next(it, 'dflt') for _ in range(1)]][4])
def after_err():
    d = deque(range(5)); it = reversed(d); next(it); d.pop()
    try:
        next(it)
    except RuntimeError as e:
        pass
    return list(it)
t('reversed_after_err', after_err)
print(chr(10).join(out))
