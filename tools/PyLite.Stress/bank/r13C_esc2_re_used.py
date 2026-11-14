# deadline: 30000
# allocmb: 64
# steps: 2000000000
import re
chunks = []
n = 0
while True:
    l = []
    for i in range(1000000):
        p = re.compile("a%d" % n)
        p.search("xa1"); p.match("a1"); p.fullmatch("a1")
        l.append(p)
        n += 1
    chunks.append(l)
