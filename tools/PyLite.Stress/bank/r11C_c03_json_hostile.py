# deadline: 20000
import json, math, decimal, collections
def t(name, f):
    try:
        r = f()
        print(name + '=' + repr(r)[:90])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:70])

t('nan', lambda: json.dumps([float('nan'), float('inf'), -float('inf'), -0.0]))
t('nan_strict', lambda: json.dumps(float('nan'), allow_nan=False))
t('load_nan', lambda: json.loads('[NaN, Infinity, -Infinity, -0.0, 1e400, -1e400, 1e-400]'))
t('load_bigint', lambda: len(str(json.loads('1' * 4300))))
t('load_bigint2', lambda: json.loads('1' * 4301))
t('load_bigfloat', lambda: json.loads('1' * 5000 + '.5'))
t('load_exp', lambda: json.loads('1e99999999999999999999'))
t('load_exp_neg', lambda: json.loads('1e-99999999999999999999'))
t('load_minus0', lambda: json.loads('-0'))
t('load_lead0', lambda: json.loads('01'))
t('load_surr', lambda: json.loads('"\\ud800"'))
t('load_surr_pair', lambda: len(json.loads('"\\ud83d\\ude00"')))
t('load_surr_half', lambda: json.loads('"\\ud83d\\u0041"'))
t('load_bad_u', lambda: json.loads('"\\u12"'))
t('load_bad_u2', lambda: json.loads('"\\uZZZZ"'))
t('load_ctrl', lambda: json.loads('"a\x01b"'))
t('load_ctrl_nonstrict', lambda: json.loads('"a\x01b"', strict=False))
t('load_bom', lambda: json.loads('﻿[]'))
t('load_bytes_bom', lambda: json.loads(b'\xef\xbb\xbf[1]'))
t('load_bytes16', lambda: json.loads('[1]'.encode('utf-16')))
t('load_bytes32', lambda: json.loads('[1]'.encode('utf-32')))
t('load_bytes_bad', lambda: json.loads(b'\xff\xff'))
t('load_empty', lambda: json.loads(''))
t('load_ws', lambda: json.loads(' \t\n\r 1 \n'))
t('load_extra', lambda: json.loads('1 2'))
t('load_dupkey', lambda: json.loads('{"a":1,"a":2}'))
t('load_hook', lambda: json.loads('{"a":[1,{"b":2}]}', object_pairs_hook=lambda p: p))
t('load_hook_raise', lambda: json.loads('{"a":1}', object_hook=lambda d: 1 / 0))
t('load_parse_int', lambda: json.loads('[1,2]', parse_int=lambda s: s * 1000000))
t('load_parse_const', lambda: json.loads('[NaN]', parse_constant=lambda s: 1 / 0))
t('load_parse_float_dec', lambda: json.loads('1.10', parse_float=decimal.Decimal))
t('load_deep_list', lambda: json.loads('[' * 100000 + ']' * 100000))
t('load_deep_obj', lambda: json.loads('{"a":' * 100000 + '1' + '}' * 100000))
t('load_deep_ok', lambda: len(json.dumps(json.loads('[' * 120 + ']' * 120))))
t('load_str_huge_esc', lambda: len(json.loads('"' + '\\u0041' * 200000 + '"')))
t('load_cls', lambda: json.loads('1', cls=json.JSONDecoder))
t('dec_raw', lambda: json.JSONDecoder().raw_decode('[1] x', 0))
t('dec_raw_idx', lambda: json.JSONDecoder().raw_decode('[1]', 2 ** 63))
t('dec_raw_neg', lambda: json.JSONDecoder().raw_decode('[1]', -1))
t('err_attrs', lambda: (lambda: [e for e in [None]])())
def err():
    try:
        json.loads('[1,\n 2,]')
    except json.JSONDecodeError as e:
        return (e.msg, e.pos, e.lineno, e.colno)
t('err_pos', err)
t('dump_keys', lambda: json.dumps({1: 1, 1.5: 2, True: 3, None: 4, float('nan'): 5}))
t('dump_key_tuple', lambda: json.dumps({(1,): 1}))
t('dump_key_tuple_skip', lambda: json.dumps({(1,): 1, 'a': 2}, skipkeys=True))
t('dump_sort_mixed', lambda: json.dumps({1: 1, 'a': 2}, sort_keys=True))
t('dump_sort_same', lambda: json.dumps({'b': 1, 'a': 2, 'A': 3, chr(0x1F600): 4, chr(0xFFFF): 5}, sort_keys=True, ensure_ascii=False))
t('dump_ascii_astral', lambda: json.dumps(chr(0x1F600) + chr(0xD800)))
t('dump_noascii_surr', lambda: json.dumps(chr(0xD800), ensure_ascii=False))
t('dump_indent_str', lambda: json.dumps([1, [2]], indent='\t'))
t('dump_indent_neg', lambda: json.dumps([1, [2]], indent=-3))
t('dump_indent_bool', lambda: json.dumps([1], indent=True))
t('dump_indent_float', lambda: json.dumps([1], indent=2.0))
t('dump_sep', lambda: json.dumps({'a': [1]}, separators=('', '')))
t('dump_sep_bad', lambda: json.dumps({'a': 1}, separators=(1, 2)))
t('dump_sep_3', lambda: json.dumps({'a': 1}, separators=(',', ':', '!')))
t('dump_default', lambda: json.dumps({1, 2}, default=sorted))
t('dump_default_self', lambda: json.dumps(object(), default=lambda o: o))
t('dump_circ', lambda: (lambda l: (l.append(l), json.dumps(l)))([]))
t('dump_circ_off', lambda: (lambda l: (l.append(l), json.dumps(l, check_circular=False)))([]))
t('dump_circ_dict', lambda: (lambda d: (d.__setitem__('a', d), json.dumps(d)))({}))
t('dump_dec', lambda: json.dumps(decimal.Decimal('1.1')))
t('dump_int_sub', lambda: json.dumps(type('I', (int,), {'__repr__': lambda s: 'X', '__str__': lambda s: 'Y'})(5)))
t('dump_float_sub', lambda: json.dumps(type('F', (float,), {'__repr__': lambda s: 'X'})(5.5)))
t('dump_str_sub', lambda: json.dumps(type('S', (str,), {'__str__': lambda s: 'X'})('ab')))
t('dump_dict_sub', lambda: json.dumps(type('D', (dict,), {'items': lambda s: [('z', 1)]})(a=1)))
t('dump_list_sub', lambda: json.dumps(type('L', (list,), {'__iter__': lambda s: iter([9])})([1])))
t('dump_tuple', lambda: json.dumps((1, (2,))))
t('dump_odict', lambda: json.dumps(collections.OrderedDict([('b', 1), ('a', 2)])))
t('dump_ntuple', lambda: json.dumps(collections.namedtuple('P', 'x y')(1, 2)))
t('dump_bool_int', lambda: json.dumps([True, False, 1 == 1]))
t('dump_big_int', lambda: len(json.dumps(10 ** 4299)))
t('dump_big_int2', lambda: json.dumps(10 ** 4300))
t('dump_float_repr', lambda: json.dumps([1e16, 1e-7, 5e-324, 1.7976931348623157e308, 0.1 + 0.2]))
t('dump_bytes', lambda: json.dumps(b'a'))
t('dump_cls', lambda: json.dumps(1, cls=type('E', (json.JSONEncoder,), {'default': lambda s, o: 1})))
t('enc_iter', lambda: list(json.JSONEncoder().iterencode([1, {'a': 2}])))
t('enc_iter_one', lambda: list(json.JSONEncoder().iterencode({'a': [1]}, _one_shot=True)))
t('dump_deep', lambda: json.dumps(eval('[' * 127 + ']' * 127)) [:5])
t('dump_deep_over', lambda: json.dumps(eval('[' * 199 + ']' * 199)) [:5])
l = []
cur = l
for i in range(5000):
    n = []
    cur.append(n)
    cur = n
t('dump_deep_built', lambda: len(json.dumps(l)))
d = {}
cur = d
for i in range(5000):
    cur['k'] = {}
    cur = cur['k']
t('dump_deep_dict', lambda: len(json.dumps(d, indent=1)))
t('dump_deep_dict_sort', lambda: len(json.dumps(d, sort_keys=True)))
class R:
    def __init__(s, n): s.n = n
def dflt(o):
    return R(o.n + 1)
t('dump_default_chain', lambda: json.dumps(R(0), default=dflt))
def dflt2(o):
    return [o]
t('dump_default_list_chain', lambda: json.dumps(R(0), default=dflt2))
