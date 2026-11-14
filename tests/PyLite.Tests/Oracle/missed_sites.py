def t(f):
    try:
        print(repr(f()))
    except Exception as e:
        print('EXC', type(e).__name__ + ':', e)
from collections import deque, namedtuple
import statistics, itertools, uuid, struct, random, xml.etree.ElementTree as ET
# G1
class K:
    def __init__(self, d):
        self.d = d
    def __eq__(self, o):
        self.d[0] = 9
        return False
    __hash__ = None
d = deque([1, 2, 3])
t(lambda: K(d) in d)
t(lambda: d.count(K(d)))
d2 = deque([1, 2, 3])
def walk_set():
    out = []
    for x in d2:
        d2[0] = 7
        out.append(x)
    return out
t(walk_set)
d3 = deque([5])
def walk_rot():
    out = []
    for x in d3:
        d3.rotate(1)
        d3.reverse()
        out.append(x)
    return out
t(walk_rot)
d4 = deque([1, 2])
def walk_rot2():
    for x in d4:
        d4.rotate(0)
t(walk_rot2)
# G4
t(lambda: 'a b'.split(None, 1, 5))
t(lambda: 'ab'.encode('utf-8', 'strict', 99))
t(lambda: statistics.mean([1], [2]))
t(lambda: statistics.mean(data=[1, 2]))
t(lambda: itertools.count(0, 1, 5))
t(lambda: statistics.covariance([1, 2], [2, 4], 1))
# G5
t(lambda: namedtuple('P', 'a\x1cb')._fields)
t(lambda: uuid.UUID(' 12345678123456781234567812345678 '))
t(lambda: float.fromhex('\xa00x1p0'))
t(lambda: float.fromhex(' 0x1p0\n'))
import urllib.parse
t(lambda: urllib.parse.unwrap('\x1c<URL:x>\x1c'))
import time
t(lambda: time.strptime('2020  01', '%Y %m')[:2])
# G6
t(lambda: struct.pack('<f', 2**200))
t(lambda: struct.pack('<f', 1e60))
t(lambda: random.shuffle([1, 2], random.random))
# G7
class C:
    n = 0
    def __init__(self, v):
        self.v = v
    def __hash__(self):
        return 1
    def __eq__(self, o):
        C.n += 1
        return self is o
a = {C(i): i for i in range(6)}
C.n = 0
b = dict(a)
print(C.n, len(b))
C.n = 0
c = a.copy()
print(C.n)
class M:
    def __init__(self, tgt):
        self.tgt = tgt
    def __hash__(self):
        return 1
    def __eq__(self, o):
        self.tgt.clear()
        return False
x = {}
y = {}
x[M(y)] = 1
y[M(y)] = 2
y[M(y)] = 3
t(lambda: len(x | y))
k1 = {1: 'a', 2: 'b', 3: 'c'}
k2 = {2: 'x', 3: 'y', 4: 'z', 5: 'w'}
print(k1.keys() & k2.keys(), k2.keys() & k1.keys(), sorted(k1.keys() & {3, 1}), sorted({3, 1} & k1.keys()), k1.items() & {(2, 'b')})
# G9 (process-independent hashes stay stable within a run)
print(hash(len) == hash(len), hash(int) == hash('int'))
class Q: pass
print(hash(Q) == hash(Q))
