import hashlib, csv, io, re, itertools
chunks = []
n = 0
try:
    while True:
        l = []
        for i in range(1000000):
            l.append(re.compile("a%d" % n))
            n += 1
        chunks.append(l)
except MemoryError:
    pass
print(n)
