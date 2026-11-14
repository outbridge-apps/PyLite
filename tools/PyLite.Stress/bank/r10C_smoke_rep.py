# repeat: 2
import re, json
d = {str(i): [i, i*2] for i in range(2000)}
s = json.dumps(d)
print(len(re.findall(r"\d+", s)))
