# deadline: 30000
import functools, collections, dataclasses, string, datetime, json
import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:80])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
D = {'k%d' % i: i for i in range(100000)}
def kw(**k):
    return len(k)
def pos(*a):
    return len(a)
def both(a, b=2, /, c=3, *, d=4, **k):
    return (a, b, c, d, len(k))
t('kw_big', lambda: kw(**D))
t('dict_kw_big', lambda: len(dict(**D)))
t('dict_upd_kw_big', lambda: len({}.update(**D) or D))
t('elem_kw_big', lambda: len(ET.Element('a', **D).attrib))
t('elem_attrib_and_kw', lambda: len(ET.Element('a', {'x': '1'}, **D).attrib))
t('fmt_kw_big', lambda: '{k5}'.format(**D))
t('fmt_map', lambda: '{k99999}'.format_map(D))
t('partial_kw_big', lambda: functools.partial(kw, **D)(z=1))
t('partial_kw_merge', lambda: functools.partial(kw, **D)(**D))
t('tmpl_kw', lambda: string.Template('$k7').substitute(**D))
t('tmpl_both', lambda: string.Template('$k7').substitute(D, k7='x'))
t('dup_kw', lambda: kw(a=1, **{'a': 2}))
t('dup_kw2', lambda: kw(**{'a': 1}, **{'a': 2}))
t('nonstr_kw', lambda: kw(**{1: 2}))
t('surr_kw', lambda: kw(**{chr(0xD800): 1}))
t('empty_kw', lambda: kw(**{'': 1}))
t('kw_to_builtin_bad', lambda: len([], **{'x': 1}))
t('kw_to_abs', lambda: abs(x=1))
t('posonly_by_kw', lambda: both(1, b=2))
t('posonly_in_kw_k', lambda: both(1, **{'a': 5}))
t('kwonly_missing', lambda: both())
t('star_huge', lambda: pos(*range(1000000)))
t('star_gen', lambda: pos(*(x for x in range(1000000))))
t('star_str', lambda: pos(*('x' * 1000000)))
t('star_dict', lambda: pos(*D))
t('star_set', lambda: pos(*set(range(100000))))
t('star_star_mapping_keys', lambda: kw(**collections.OrderedDict(D)))
t('star_star_counter', lambda: kw(**collections.Counter(D)))
t('star_star_chainmap', lambda: kw(**collections.ChainMap(D, {'zz': 1})))
t('star_star_defaultdict', lambda: kw(**collections.defaultdict(int, D)))
class M:
    def keys(self):
        return ['a', 'b']
    def __getitem__(self, k):
        return k * 2
t('star_star_script_map', lambda: kw(**M()))
class MK:
    def keys(self):
        return [1]
    def __getitem__(self, k):
        return k
t('star_star_script_badkey', lambda: kw(**MK()))
class MR:
    def keys(self):
        raise KeyError('boom')
    def __getitem__(self, k):
        return k
t('star_star_script_raise', lambda: kw(**MR()))
class MG:
    def keys(self):
        return ['a']
    def __getitem__(self, k):
        raise IndexError(k)
t('star_star_getitem_raise', lambda: kw(**MG()))
t('star_star_nonmap', lambda: kw(**[1]))
t('star_nonit', lambda: pos(*1))
t('dc_kw_big', lambda: dataclasses.make_dataclass('X', ['a'])(**D))
t('nt_kw', lambda: collections.namedtuple('P', 'a b')(**{'a': 1, 'b': 2, 'c': 3}))
t('nt_replace_big', lambda: collections.namedtuple('P', 'a b')(1, 2)._replace(**D))
t('dt_replace_kw', lambda: datetime.datetime(2020, 1, 1).replace(**{'year': 2021, 'zz': 1}))
t('json_kw', lambda: json.dumps(1, **{'indent': 2, 'zz': 1}))
t('print_kw', lambda: print(1, **{'sep': '', 'zz': 1}))
t('sorted_kw', lambda: sorted([2, 1], **{'key': None, 'reverse': 1}))
t('sorted_kw_bad', lambda: sorted([2, 1], **{'cmp': None}))
t('max_kw', lambda: max([1], **{'default': 0, 'key': None}))
t('int_kw', lambda: int('10', **{'base': 2}))
t('int_kw_x', lambda: int(**{'x': '10'}))
t('str_kw', lambda: str(**{'object': 1}))
t('range_kw', lambda: range(**{'stop': 1}))
t('lambda_defaults_many', lambda: (lambda *a, **k: (len(a), len(k)))(*range(1000), **D))
t('method_kw_big', lambda: [].sort(**{'key': None}))
t('call_depth_kw', lambda: functools.reduce(lambda f, i: functools.partial(f, **{'k%d' % i: i}), range(2000), kw)())
print(chr(10).join(out))
