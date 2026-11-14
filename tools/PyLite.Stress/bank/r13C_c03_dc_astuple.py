# deadline: 20000
import io, traceback, copy, json, collections, functools, dataclasses, operator, itertools
import xml.etree.ElementTree as ET
N = 100000
@dataclasses.dataclass(frozen=True)
class C:
    x: object
a = None
for i in range(N):
    a = C(a)
try:
    r = dataclasses.astuple(a)
    print("ok", repr(r)[:60] if not isinstance(r, (list, tuple, dict)) else type(r).__name__)
except Exception as e:
    print("exc", type(e).__name__, str(e)[:80])
