# deadline: 20000
# allocmb: 1024
import json
s = '[' + '1.5,' * 300000 + '1.5]'
print(len(json.loads(s, parse_float=lambda x: x)))
