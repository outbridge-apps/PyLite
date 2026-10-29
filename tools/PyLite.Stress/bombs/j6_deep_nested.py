# deadline: 20000
# allocmb: 1024
import json
try:
    json.loads('[' * 100000 + ']' * 100000)
except ValueError as e:
    print('clean', type(e).__name__)
