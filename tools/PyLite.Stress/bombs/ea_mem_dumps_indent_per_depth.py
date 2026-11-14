# deadline: 3000
# allocmb: 256
import json
n = [0]
def f(o):
    n[0] += 1
    if n[0] < 120:
        return iter([])
    return [1]
s = json.dumps(iter([]), indent='x' * 5000, default=f)
