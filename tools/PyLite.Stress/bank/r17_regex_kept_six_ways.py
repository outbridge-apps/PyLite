# Review 12, row 14: kept patterns used six ways are charged for every regex they build.
# allocmb: 64
# deadline: 20000
# steps: 2000000000
import re
risky = "ab" + chr(0x300) + "cd"
plain = "abcd"
kept = []
n = 0
try:
    for i in range(60000):
        p = re.compile(r"\b\w+%d|x\b" % i)
        p.match(plain)
        p.fullmatch(plain)
        p.search(plain)
        p.match(risky)
        p.fullmatch(risky)
        p.search(risky)
        kept.append(p)
        n += 1
except MemoryError:
    pass
print(n, len(kept))
