# deadline: 20000
import io, traceback, copy, json, collections, functools, dataclasses, operator, itertools
import xml.etree.ElementTree as ET
N = 100000
P = collections.namedtuple("P", "x")
a = None
for i in range(N):
    a = P(a)
try:
    r = a == P(a.x)
    print("ok", repr(r)[:60] if not isinstance(r, (list, tuple, dict)) else type(r).__name__)
except Exception as e:
    print("exc", type(e).__name__, str(e)[:80])
