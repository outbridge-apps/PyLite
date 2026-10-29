# deadline: 20000
# allocmb: 1024
import json
s = '[' + '1,' * 1500000 + '1] x'
try:
    json.loads(s)
except ValueError as e:
    print(type(e).__name__)
