# deadline: 30000
import collections, copy, itertools
from collections import deque, Counter, OrderedDict, defaultdict, ChainMap
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:90])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
def rot(n, d=None):
    d = deque(range(5)) if d is None else d
    d.rotate(n)
    return d
t('rot_2_63m1', lambda: rot(2**63 - 1))
print(chr(10).join(out))
