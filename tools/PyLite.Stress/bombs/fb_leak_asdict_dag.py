# deadline: 3000
# allocmb: 256
from dataclasses import dataclass, asdict
@dataclass
class N:
    a: object
    b: object
x = N(None, None)
for i in range(40):
    x = N(x, x)
asdict(x)
