import json
from decimal import Decimal

out = []
data = {'b': [1, 2.5, None, True], 'a': {'x': 'é', 'y': 'tab\there'}, 'c': []}
out.append(json.dumps(data, sort_keys=True))
out.append(json.dumps(data, sort_keys=True, indent=2))
out.append(json.dumps(data, sort_keys=True, separators=(',', ':')))
out.append(json.dumps(data, sort_keys=True, ensure_ascii=False))
out.append(json.dumps([1, 2], indent='\t'))
out.append(json.dumps({'d': Decimal('1.10')}, default=str))
out.append(json.dumps({1: 'int key'}))
out.append(json.dumps({2.5: 'float key'}))
out.append(json.dumps({None: 'none'}))
out.append(json.loads('{"a": [1, 2.0, -3e2, true, null]}'))
out.append(json.loads('"\\u00e9\\ud83d\\ude00"') == 'é\U0001F600')
out.append(json.loads('{"p": 1.5}', parse_float=Decimal))
out.append(json.loads('[1, 2]', parse_int=float))
out.append(json.loads('{"a": 1}', object_hook=lambda d: sorted(d.items())))
out.append(json.loads('[NaN, Infinity]', parse_constant=str))
out.append(json.dumps(float('inf')))
out.append(json.dumps('</script>'))
result = out
