# repeat: 2
import re
subj = "caf" + chr(0xe9) + " x" + chr(0xb2) + " " + chr(0x301) + " word " * 20
t = 0
for k in range(200):
    p = re.compile(r"\b\w+%d\b|\bword\b" % k)
    t += len(p.findall(subj)) + bool(p.match(subj)) + bool(p.fullmatch(subj))
print(t)
