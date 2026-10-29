# deadline: 20000
# allocmb: 1024
import json
l = []
for _ in range(100000):
    l = [l]
try:
    print(len(json.dumps(l)))
except (RecursionError, ValueError) as e:
    print('clean', type(e).__name__)
