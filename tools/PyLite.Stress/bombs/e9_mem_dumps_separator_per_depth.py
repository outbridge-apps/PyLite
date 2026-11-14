# deadline: 3000
# allocmb: 256
import json
x = 1
for i in range(100):
    x = [x]
s = json.dumps(x, separators=('y' * 2000000, ':'), indent=0)
