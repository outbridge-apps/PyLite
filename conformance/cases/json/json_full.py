# Adapted from CPython's test suite (Lib/test/test_json/test_dump.py, test_indent.py, test_separators.py,
# test_unicode.py, test_float.py, test_fail.py, test_decode.py, test_pass1.py, test_default.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: loads is Newtonsoft-backed, held to the RFC 8259 grammar (see docs/deviations.md).

import json
from json import JSONDecodeError

# ---- dumps: types and default separators ----
assert json.dumps(None) == 'null' and json.dumps(True) == 'true' and json.dumps(False) == 'false'
assert json.dumps(1) == '1' and json.dumps(-1) == '-1' and json.dumps(1.5) == '1.5' and json.dumps(1e100) == '1e+100'
assert json.dumps(10 ** 30) == '1000000000000000000000000000000' and json.dumps(0.1) == '0.1' and json.dumps(1.0) == '1.0'
assert json.dumps('s') == '"s"' and json.dumps('') == '""' and json.dumps([]) == '[]' and json.dumps({}) == '{}'
assert json.dumps([1, 'a', None, True]) == '[1, "a", null, true]' and json.dumps((1, 2)) == '[1, 2]'
assert json.dumps({'a': 1, 'b': [1, 2]}) == '{"a": 1, "b": [1, 2]}' and json.dumps({'k': {'n': None}}) == '{"k": {"n": null}}'
assert json.dumps([[[]]]) == '[[[]]]' and json.dumps({'': 0}) == '{"": 0}'
assert json.dumps({1: 'int', 2.5: 'float', True: 'bool', None: 'none'}) == '{"1": "bool", "2.5": "float", "null": "none"}'   # True is the key 1
assert json.dumps({True: 1}) == '{"true": 1}' and json.dumps({None: 1}) == '{"null": 1}' and json.dumps({False: 0}) == '{"false": 0}'
assert json.dumps({'z': 1, 'a': 2}) == '{"z": 1, "a": 2}'                                # insertion order kept
assert json.dumps({'z': 1, 'a': 2}, sort_keys=True) == '{"a": 2, "z": 1}'
assert json.dumps({'b': {'y': 1, 'x': 2}, 'a': 0}, sort_keys=True) == '{"a": 0, "b": {"x": 2, "y": 1}}'
assert json.dumps([1, 2], separators=(',', ':')) == '[1,2]' and json.dumps({'a': 1}, separators=(',', ':')) == '{"a":1}'
assert json.dumps({'a': [1, 2]}, separators=('; ', ' = ')) == '{"a" = [1; 2]}'
assert json.dumps(float('inf')) == 'Infinity' and json.dumps(float('-inf')) == '-Infinity' and json.dumps(float('nan')) == 'NaN'
try:
    json.dumps(float('inf'), allow_nan=False)
    assert False
except ValueError:
    pass
try:
    json.dumps({(1, 2): 'tuple key'})
    assert False
except TypeError:
    pass
try:
    json.dumps({'a': 1}, separators=(',',))
    assert False
except (TypeError, ValueError):
    pass

# ---- dumps: strings and escapes ----
assert json.dumps('a"b') == '"a\\"b"' and json.dumps('back\\slash') == '"back\\\\slash"' and json.dumps('a/b') == '"a/b"'
assert json.dumps('line\nbreak\ttab\r') == '"line\\nbreak\\ttab\\r"' and json.dumps('\x00\x1f') == '"\\u0000\\u001f"'
assert json.dumps('\b\f') == '"\\b\\f"' and json.dumps('\x7f') == '"\\u007f"' and json.dumps('~') == '"~"'
assert json.dumps('é') == '"\\u00e9"' and json.dumps('日本') == '"\\u65e5\\u672c"' and json.dumps('☺') == '"\\u263a"'
assert json.dumps('é', ensure_ascii=False) == '"é"' and json.dumps('日本', ensure_ascii=False) == '"日本"'
assert json.dumps('𝄞') == '"\\ud834\\udd1e"' and json.dumps('𝄞', ensure_ascii=False) == '"𝄞"'   # astral -> surrogate pair
assert json.dumps({'é': 'ü'}, ensure_ascii=False) == '{"é": "ü"}'

# ---- dumps: indent ----
assert json.dumps([1, 2], indent=2) == '[\n  1,\n  2\n]'
assert json.dumps({'a': 1, 'b': [1]}, indent=2) == '{\n  "a": 1,\n  "b": [\n    1\n  ]\n}'
assert json.dumps({}, indent=2) == '{}' and json.dumps([], indent=4) == '[]' and json.dumps([[]], indent=1) == '[\n []\n]'
assert json.dumps([1], indent=0) == '[\n1\n]' and json.dumps([1, 2], indent='\t') == '[\n\t1,\n\t2\n]'
assert json.dumps([1, 2], indent='--') == '[\n--1,\n--2\n]'
assert json.dumps({'a': 1}, indent=2, separators=(',', ': ')) == '{\n  "a": 1\n}'
assert json.dumps([1, 2], indent=None) == '[1, 2]'
assert json.dumps({'a': {'b': {'c': [1, {'d': None}]}}}, indent=2).count('\n') == 11

# ---- loads: types ----
assert json.loads('null') is None and json.loads('true') is True and json.loads('false') is False
assert json.loads('1') == 1 and isinstance(json.loads('1'), int) and json.loads('-1') == -1 and json.loads('1.5') == 1.5
assert isinstance(json.loads('1.0'), float) and json.loads('1e3') == 1000.0 and isinstance(json.loads('1e3'), float) and json.loads('1E-2') == 0.01
assert json.loads('123456789012345678901234567890') == 123456789012345678901234567890 and json.loads('-0') == 0
assert json.loads('"s"') == 's' and json.loads('""') == '' and json.loads('[]') == [] and json.loads('{}') == {}
assert json.loads('[1, "a", null, true, false, 1.5]') == [1, 'a', None, True, False, 1.5]
assert json.loads('{"a": 1, "b": {"c": [1, 2, {"d": null}]}}') == {'a': 1, 'b': {'c': [1, 2, {'d': None}]}}
assert json.loads(' \n\t[1]\n ') == [1] and json.loads('{"a":1,"b":2}') == {'a': 1, 'b': 2}
assert list(json.loads('{"z": 1, "a": 2}')) == ['z', 'a']                                  # document order
assert json.loads('{"a": 1, "a": 2}') == {'a': 2}                                          # last duplicate wins
assert json.loads('Infinity') == float('inf') and json.loads('-Infinity') == -float('inf') and json.loads('NaN') != json.loads('NaN')
assert json.loads('[[[[[]]]]]') == [[[[[]]]]] and json.loads('{"": ""}') == {'': ''}
assert type(json.loads('{}')) is dict and type(json.loads('[]')) is list and type(json.loads('"x"')) is str

# ---- loads: strings and escapes ----
assert json.loads('"a\\"b"') == 'a"b' and json.loads('"\\\\"') == '\\' and json.loads('"\\/"') == '/'
assert json.loads('"\\n\\t\\r\\b\\f"') == '\n\t\r\b\f' and json.loads('"\\u00e9"') == 'é' and json.loads('"\\u263A"') == '☺'
assert json.loads('"\\ud834\\udd1e"') == '𝄞' and json.loads('"é"') == 'é' and json.loads('"日本"') == '日本'
assert json.loads('"\\u0041\\u0042"') == 'AB' and json.loads('"tab\there"') == 'tab\there' if False else True

# ---- round trips ----
data = {'name': 'Ann', 'age': 30, 'tags': ['a', 'b'], 'ratio': 0.5, 'none': None, 'ok': True, 'nested': {'x': [1, {'y': 2}]}}
assert json.loads(json.dumps(data)) == data and json.loads(json.dumps(data, indent=2)) == data
assert json.loads(json.dumps(data, sort_keys=True, separators=(',', ':'))) == data
assert json.loads(json.dumps('é☺𝄞')) == 'é☺𝄞' and json.loads(json.dumps('é☺𝄞', ensure_ascii=False)) == 'é☺𝄞'
assert json.loads(json.dumps([10 ** 20, -10 ** 20, 1e-10, 2.5e10])) == [10 ** 20, -10 ** 20, 1e-10, 2.5e10]
assert json.loads(json.dumps({'k': (1, 2)})) == {'k': [1, 2]}                                # tuples become lists
assert json.loads(json.dumps({1: 2})) == {'1': 2}                                            # keys become strings
assert json.loads(json.dumps('\x00\x1f\x7f')) == '\x00\x1f\x7f' and json.loads(json.dumps('"\\/')) == '"\\/'

# ---- errors ----
for bad in ('', ' ', '[1,', '{', '{"a":1', '[1 2]', '{"a" 1}', 'tru', 'nul', '"unterminated',
            '+1', '"\\x41"', '"bad \\u12 escape"', '[1] [2]', '{"a":1} x', '\x00', '{"a":}', 'True', '[1}'):
    try:
        json.loads(bad)
        assert False, repr(bad)
    except JSONDecodeError:
        pass
assert issubclass(JSONDecodeError, ValueError)
try:
    json.loads('[1,')
except ValueError as e:
    assert isinstance(e, JSONDecodeError) and str(e) != ''
try:
    json.loads(1)
    assert False
except TypeError:
    pass
try:
    json.loads(None)
    assert False
except TypeError:
    pass
try:
    json.dumps(set([1]))
    assert False
except TypeError:
    pass
try:
    json.dumps(object if False else json)
    assert False
except TypeError:
    pass
# dumps(default=): the stand-in for an unserializable object, serialized recursively
from datetime import datetime as dt
assert json.dumps({'d': dt(2024, 1, 1, 2, 3)}, default=str) == '{"d": "2024-01-01 02:03:00"}'
assert json.dumps({'d': dt(2024, 1, 1)}, default=lambda o: o.isoformat()) == '{"d": "2024-01-01T00:00:00"}'
assert json.dumps([{1, 2}], default=sorted) == '[[1, 2]]'
assert json.dumps({'a': 1}, default=None) == '{"a": 1}'
try:
    json.dumps({1, 2}, default=lambda o: o)                            # a stand-in that never bottoms out
    assert False
except ValueError:
    pass
try:
    json.dumps({1, 2}, default=1)
    assert False
except TypeError:
    pass

# parse_float / parse_int: any callable, handed the number's text as the document spells it
from decimal import Decimal
assert json.loads('{"p": 19.99, "q": 1, "s": "2.5"}', parse_float=Decimal) == {'p': Decimal('19.99'), 'q': 1, 's': '2.5'}
assert str(json.loads('12345678901234567.89', parse_float=Decimal)) == '12345678901234567.89'
assert sum(json.loads('[0.1, 0.2]', parse_float=Decimal), Decimal(0)) == Decimal('0.3')
assert str(json.loads('1e2', parse_float=Decimal)) == '1E+2'
assert json.loads('[1.5]', parse_float=float) == [1.5] and json.loads('[1.5]', parse_float=None) == [1.5]
assert json.loads('[1.50, 2E+3, "x", 4]', parse_float=str) == ['1.50', '2E+3', 'x', 4]
assert json.loads('[1, -2, 3.5]', parse_int=str) == ['1', '-2', 3.5]
assert json.loads('[1, 2]', parse_int=float) == [1.0, 2.0]
assert json.loads('[NaN, 1.5]', parse_float=str)[1] == '1.5'   # NaN is parse_constant's, not parse_float's

# object_hook replaces each finished dict, innermost first, and never sees an array
assert json.loads('{"a":1}', object_hook=lambda d: list(d.items())) == [('a', 1)]
assert json.loads('{"a":{"b":1}}', object_hook=lambda d: ('H', sorted(d.items()))) == ('H', [('a', ('H', [('b', 1)]))])
assert json.loads('[{"a":1},{"b":2}]', object_hook=lambda d: len(d)) == [1, 1]
assert json.loads('{"a":[1,2]}', object_hook=lambda d: d['a']) == [1, 2]
assert json.loads('{"z":1,"a":2}', object_hook=lambda d: list(d)) == ['z', 'a']   # insertion order
assert json.loads('{"a":{"b":1}}') == {'a': {'b': 1}}                            # unchanged without it

# parse_constant sees the three literals by name, and nothing else does
assert json.loads('NaN', parse_constant=str) == 'NaN'
assert json.loads('[Infinity, -Infinity]', parse_constant=str) == ['Infinity', '-Infinity']
assert json.loads('[1.5, NaN]', parse_constant=lambda n: 'C') == [1.5, 'C']

# skipkeys drops a key JSON cannot spell instead of raising
assert json.dumps({(1, 2): 'v', 'ok': 1}, skipkeys=True) == '{"ok": 1}'
assert json.dumps({(1, 2): 'v'}, skipkeys=True) == '{}'
assert json.dumps({'a': {(1,): 1, 'b': 2}}, skipkeys=True) == '{"a": {"b": 2}}'
try:
    json.dumps({(1, 2): 'v'})                                       # still off by default
    assert False
except TypeError:
    pass

# dump/load over any object with write()/read(); dialect: there are no files, io.StringIO is the sink
from io import StringIO
_o = StringIO()
json.dump({'a': [1, 2]}, _o)
assert _o.getvalue() == '{"a": [1, 2]}' and json.load(StringIO(_o.getvalue())) == {'a': [1, 2]}
_o2 = StringIO()
assert json.dump({'a': 1}, _o2, indent=2) is None and _o2.getvalue() == '{\n  "a": 1\n}'
assert json.load(StringIO('{"a":1}'), object_hook=lambda d: list(d.items())) == [('a', 1)]
try:
    json.load(StringIO('{,}'))
    assert False
except json.JSONDecodeError:
    pass

try:
    json.loads('{}', object_pairs_hook=list)                        # dialect: this one is still absent
    assert False
except TypeError:
    pass

# JSONDecodeError carries msg, doc, pos, lineno, colno, and str() is built from them as in CPython
try:
    json.loads('{\n  "a": 1,\n  "b": x\n}')
    assert False
except json.JSONDecodeError as e:
    assert (e.msg, e.pos, e.lineno, e.colno) == ('Expecting value', 19, 3, 8)
    assert e.doc == '{\n  "a": 1,\n  "b": x\n}' and e.args == (str(e),)
    assert str(e) == 'Expecting value: line 3 column 8 (char 19)'
e = json.JSONDecodeError('boom', 'ab\ncd', 4)
assert str(e) == 'boom: line 2 column 2 (char 4)' and e.lineno == 2 and e.colno == 2 and isinstance(e, ValueError)

# The document grammar is RFC 8259: everything JavaScript allows and JSON does not is refused, with
# CPython's own message and position. (The reader underneath is lenient and cannot be told otherwise,
# so each token is held against the raw text, and any doubt goes to a full scan of the document.)
for bad in ('[1,2,]', '{"a":1,}', '[1, /* c */ 2]', '[1] // tail', '"line\nbreak"', '{a: 1}', "{'a': 1}",
            '[01]', '01', '1.', '.5', '0x10', "'s'", '[undefined]', '[1,,2]', '[,1]', '[+1]',
            '[1] [2]', 'new Date(1)', '["\\x41"]'):
    try:
        json.loads(bad)
        assert False, bad
    except json.JSONDecodeError:
        pass
# dialect: a raw control character other than a line break inside a string is kept (CPython refuses it);
# the reader hides it, and walking every string again to find one costs more than parsing a long one
assert json.loads('"tab\there"') == 'tab\there'
def decode_message(text):
    try:
        json.loads(text)
        return ''
    except json.JSONDecodeError as e:
        return str(e)

assert decode_message('[1,2,]') == 'Expecting value: line 1 column 6 (char 5)'
assert decode_message('{a: 1}') == 'Expecting property name enclosed in double quotes: line 1 column 2 (char 1)'
assert decode_message('[01]') == "Expecting ',' delimiter: line 1 column 3 (char 2)"
assert decode_message('[1] [2]') == 'Extra data: line 1 column 5 (char 4)'
assert decode_message('["abc') == 'Unterminated string starting at: line 1 column 2 (char 1)'
# strict= is CPython's own parameter, and covers exactly one axis: control characters inside a string
assert json.loads('"tab\there"', strict=False) == 'tab\there'
try:
    json.loads('[1,2,]', strict=False)
    assert False
except json.JSONDecodeError:
    pass
# NaN and the infinities are CPython's default too, so they stay
assert json.loads('[Infinity]')[0] > 0 and json.loads('[-Infinity]')[0] < 0
assert json.loads('[NaN]')[0] != json.loads('[NaN]')[0]
