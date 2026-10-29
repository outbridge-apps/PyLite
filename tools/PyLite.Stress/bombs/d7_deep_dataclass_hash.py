# deadline: 20000
# allocmb: 1024
from dataclasses import dataclass
@dataclass(frozen=True)
class N:
    child: object
a = None
for _ in range(100000):
    a = N(a)
try:
    print(hash(a) is not None)
except RecursionError as e:
    print('clean RecursionError')
