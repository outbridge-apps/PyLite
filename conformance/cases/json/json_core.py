# Adapted from CPython's test suite (Lib/test/test_json/).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import json

# dumps: default separators are ', ' and ': '
assert json.dumps({'a': 1}) == '{"a": 1}'
assert json.dumps([1, 2]) == '[1, 2]'
assert json.dumps('text') == '"text"'
assert json.dumps(True) == 'true'
assert json.dumps(False) == 'false'
assert json.dumps(None) == 'null'
assert json.dumps(1.5) == '1.5'
assert json.dumps({}) == '{}'
assert json.dumps([]) == '[]'

# compact separators and sorted keys
assert json.dumps({'b': 1, 'a': 2}, sort_keys=True) == '{"a": 2, "b": 1}'
assert json.dumps({'a': [1, 2]}, separators=(',', ':')) == '{"a":[1,2]}'

# non-ASCII escapes by default; ensure_ascii=False keeps the character
assert json.dumps('é') == '"\\u00e9"'
assert json.dumps('é', ensure_ascii=False) == '"é"'

# loads
assert json.loads('{"a": 1, "b": [true, null]}') == {'a': 1, 'b': [True, None]}
assert json.loads('42') == 42
assert json.loads('-3.5') == -3.5
assert json.loads('1e2') == 100.0
assert json.loads('"s"') == 's'
assert json.loads('[]') == []
assert json.loads('"\\u0041"') == 'A'

# ints stay ints, floats stay floats
assert json.loads('1') == 1 and isinstance(json.loads('1'), int)
assert isinstance(json.loads('1.0'), float)

# round trip preserves structure and key order
src = {'z': 1, 'a': {'nested': [1, 'x', None, True]}}
assert json.loads(json.dumps(src)) == src
assert list(json.loads(json.dumps(src))) == ['z', 'a']

# invalid input raises
raised = False
try:
    json.loads('{bad json')
except Exception:
    raised = True
assert raised

raised = False
try:
    json.loads('')
except Exception:
    raised = True
assert raised
