# deadline: 20000
import io, traceback, copy, json, collections, functools, dataclasses, operator, itertools
import xml.etree.ElementTree as ET
N = 100000
k = functools.cmp_to_key(lambda a, b: 0)
x = 1
for i in range(N):
    x = k(x)
try:
    r = sorted([x, x])
    print("ok", repr(r)[:60] if not isinstance(r, (list, tuple, dict)) else type(r).__name__)
except Exception as e:
    print("exc", type(e).__name__, str(e)[:80])
