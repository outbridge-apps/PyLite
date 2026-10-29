from contextlib import suppress, closing
from decimal import Decimal, getcontext, localcontext, ROUND_DOWN
log = []
for i in range(3):
    with suppress(KeyError, IndexError):
        log.append(i)
        [][i] if i % 2 else {}[i]
        log.append('never')
class Res:
    def __init__(self):
        self.closed = False
    def close(self):
        self.closed = True
r = Res()
with closing(r) as same:
    inside = same is r and not r.closed
before = getcontext().prec
with localcontext() as c:
    c.prec = 5
    third = str(Decimal(1) / Decimal(3))
    c.rounding = ROUND_DOWN
    down = str(Decimal('2.675').quantize(Decimal('0.01')))
after = str(Decimal(1) / Decimal(3))
try:
    with localcontext() as c:
        c.prec = 3
        raise ValueError('x')
except ValueError:
    restored = getcontext().prec == before
result = [log, inside, r.closed, third, down, after, restored]
