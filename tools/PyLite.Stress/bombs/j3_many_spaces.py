# deadline: 20000
# allocmb: 1024
import json
s = '[' + ('          1,' * 400000) + '1]'
print(len(json.loads(s)))
