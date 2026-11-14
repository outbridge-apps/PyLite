# deadline: 3000
import json
d = {('k' * 2000) + str(i): 0 for i in range(3000)}
for i in range(1000):
    json.dumps(d, sort_keys=True)
