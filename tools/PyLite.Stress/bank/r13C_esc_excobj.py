# deadline: 60000
# allocmb: 64
# steps: 2000000000
import hashlib, csv, io, re, itertools
chunks = []
n = 0
try:
    while True:
        l = []
        for i in range(1000000):
            l.append(ValueError(i))
            n += 1
        chunks.append(l)
except MemoryError:
    pass
print(n)
