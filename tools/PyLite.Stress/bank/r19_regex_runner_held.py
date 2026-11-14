# deadline: 10000
# Review 13, row 27: 1,000 kept patterns of 500 optional parts, used every way; a regex that has run keeps a runner
# sized by its nodes (about 33 KB each here), held from its first run.
import re
keep = []
for i in range(1000):
    c = re.compile("a?" * 500 + "z%d" % i)
    s = "z%d" % i
    c.search(s); c.match(s); c.fullmatch(s)
    keep.append(c)
print(len(keep))
