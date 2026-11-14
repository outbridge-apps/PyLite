# deadline: 60000
# allocmb: 100
import re
subj = "x² word"
n = 0
pats = []
for k in range(1500):
    words = ["w%d_%d" % (k, i) for i in range(300)]
    p = re.compile(r"\b(?:" + "|".join(words) + r")\b")
    pats.append(p)
    if p.match(subj):
        n += 1
    if p.fullmatch(subj):
        n += 1
    if p.search(subj):
        n += 1
print(n, len(pats))
