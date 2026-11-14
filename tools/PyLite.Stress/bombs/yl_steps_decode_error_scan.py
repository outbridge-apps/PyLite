# steps: 20000
# deadline: 3000
# allocmb: 64
import json
d = 'x' * 16000000
for i in range(300):
    e = json.JSONDecodeError('m', d, len(d))
