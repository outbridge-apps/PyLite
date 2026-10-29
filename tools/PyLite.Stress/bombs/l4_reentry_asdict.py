# datadepth: 1000000
# deadline: 20000
# allocmb: 1024
from dataclasses import dataclass, asdict
@dataclass
class N:
    n: object = None
a = N()
for i in range(200000):
    a = N(a)
try:
    asdict(a)
except RecursionError:
    print('clean RecursionError')
