# typed models: dataclasses with typing annotations, enums, textwrap reports, sorting by attrgetter
from dataclasses import dataclass, field, asdict, replace
from typing import List, Optional, Dict
from enum import Enum, IntEnum
from operator import attrgetter, itemgetter
import textwrap, time
class Status(Enum):
    OPEN = 'open'
    PAID = 'paid'
    LATE = 'late'
class Prio(IntEnum):
    LOW = 1
    HIGH = 2
@dataclass(order=True)
class Line:
    sku: str
    qty: int = 1
    price: float = 0.0
@dataclass
class Invoice:
    no: int
    cust: str
    status: Status = Status.OPEN
    prio: Prio = Prio.LOW
    lines: List[Line] = field(default_factory=list)
    note: Optional[str] = None
    meta: Dict[str, int] = field(default_factory=dict)
    def total(self) -> float:
        return sum(l.qty * l.price for l in self.lines)
t0 = time.perf_counter()
invs = []
for i in range(8000):
    inv = Invoice(i, 'C%03d' % (i % 300), list(Status)[i % 3], Prio(i % 2 + 1))
    for j in range(4):
        inv.lines.append(Line('S%d' % (i * 4 + j), j + 1, (i % 50) + j * 0.25))
    invs.append(inv)
t1 = time.perf_counter()
invs.sort(key=attrgetter('cust', 'no'))
late = [replace(v, note='late %d' % v.no) for v in invs if v.status is Status.LATE]
d = [asdict(v) for v in invs[:1500]]
top = sorted(invs, key=lambda v: (-v.total(), v.no))[:3]
rep = '\n'.join(textwrap.fill('Invoice %d for %s has %d lines totalling %.2f and status %s' % (v.no, v.cust, len(v.lines), v.total(), v.status.value), width=30) for v in invs[:2000])
lines = sorted(l for v in invs[:2000] for l in v.lines)[:2]
t2 = time.perf_counter()
print(len(late), len(d), d[0]['lines'][0], [v.no for v in top], len(rep), lines[0], 'build', round(t1 - t0, 3), 'work', round(t2 - t1, 3))
