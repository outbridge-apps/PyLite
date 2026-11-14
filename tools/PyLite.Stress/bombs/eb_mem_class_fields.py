# deadline: 3000
# allocmb: 256
from dataclasses import make_dataclass
L = []
for i in range(150):
    L.append(make_dataclass('C', ['a%d' % j for j in range(35000)]))
