# Adapted from CPython's test suite (Lib/test/test_json/).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import json

# indent output format
assert json.dumps({'a': 1}, indent=2) == '{\n  "a": 1\n}'
assert json.dumps([1, 2], indent=2) == '[\n  1,\n  2\n]'
assert json.dumps({}, indent=2) == '{}'
assert json.dumps([], indent=2) == '[]'
nested = json.dumps({'a': [1]}, indent=2)
assert nested == '{\n  "a": [\n    1\n  ]\n}'

# string escaping both ways
assert json.dumps('a"b') == '"a\\"b"'
assert json.dumps('a\\b') == '"a\\\\b"'
assert json.dumps('line1\nline2') == '"line1\\nline2"'
assert json.dumps('tab\there') == '"tab\\there"'
assert json.loads('"a\\"b"') == 'a"b'
assert json.loads('"a\\\\b"') == 'a\\b'
assert json.loads('"a\\nb"') == 'a\nb'
assert json.loads('"\\t"') == '\t'

# unicode round trip
assert json.loads(json.dumps('привет', ensure_ascii=False)) == 'привет'
assert json.loads(json.dumps('привет')) == 'привет'
assert json.loads('"\\u0416"') == 'Ж'

# numbers: big ints, exponents, negative zero-ish
assert json.loads('123456789012345678901234567890') == 123456789012345678901234567890
assert json.dumps(2 ** 70) == '1180591620717411303424'
assert json.loads('-1.5e-3') == -0.0015
assert json.loads('0.0') == 0.0

# whitespace tolerance in loads
assert json.loads('  { "a" : [ 1 , 2 ] }  ') == {'a': [1, 2]}

# non-string dict keys are stringified by dumps
assert json.dumps({1: 'one'}) == '{"1": "one"}'
assert json.dumps({True: 1}) == '{"true": 1}'

# deep nesting round trip
deep = {'l1': {'l2': {'l3': [{'l4': 'v'}]}}}
assert json.loads(json.dumps(deep)) == deep

# lists of mixed scalars
mixed = [1, 1.5, 'x', True, False, None]
assert json.loads(json.dumps(mixed)) == mixed

# sort_keys with nested dicts sorts every level
out = json.dumps({'b': {'d': 1, 'c': 2}, 'a': 3}, sort_keys=True)
assert out == '{"a": 3, "b": {"c": 2, "d": 1}}'
