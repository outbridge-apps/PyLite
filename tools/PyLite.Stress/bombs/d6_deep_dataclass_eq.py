# deadline: 20000
# allocmb: 1024
from dataclasses import dataclass
@dataclass
class N:
    child: object
a = None; b = None
for _ in range(100000):
    a = N(a); b = N(b)
try:
    print(a == b)
except RecursionError as e:
    print('clean RecursionError')
