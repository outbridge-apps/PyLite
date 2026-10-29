# deadline: 20000
# allocmb: 1024
import json
s = '[' + ('"abcdefgh",' * 400000) + '"z"]'
print(len(json.loads(s)))
