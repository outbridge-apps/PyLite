# allocmb: 64
# deadline: 20000
import json
print(len(json.dumps(list(range(1000000)), indent=40)))
