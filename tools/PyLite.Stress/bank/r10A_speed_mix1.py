from decimal import Decimal as D
import re
import xml.etree.ElementTree as ET
from operator import itemgetter
acc = D(0)
for i in range(1, 20000):
    x = D(i) / D(7)
    acc += (D(i) // D(3)) + (D(i) % D("2.5")) + D("1.01") ** 3 + divmod(D(i), D(13))[1]
print(acc)
text = ("INFO 2024-01-01 user=bob action=login ok\n" * 3000)
r = re.compile(r"(\w+) (\S+) user=(\w+) action=(\w+)")
n = 0
pos = 0
lines = text.split("\n")
for ln in lines:
    m = r.match(ln)
    if m:
        n += len(m.group(3))
p2 = re.compile(r"\w+")
end = 0
cnt = 0
while True:
    m = p2.search(text, end, min(len(text), end + 50))
    if not m:
        break
    cnt += 1
    end = m.end()
print(n, cnt, len(re.findall(r"\b\w+=\w+", text, 0)))
root = ET.fromstring("<r>" + "".join("<a><b>t%d</b><c>%d</c></a>" % (i % 50, i) for i in range(3000)) + "</r>")
print(len(root.findall("a[b='t7']")), len(root.findall("a[.='t70']")), len(root.findall("a[last()]")))
rows = [(i, str(i)) for i in range(30000)]
print(sorted(rows, key=itemgetter(1))[5], ",".join([str(i) for i in range(30000)])[:20])
def f(*a):
    return len(a)
t = 0
lst = list(range(50))
for i in range(20000):
    t += f(*lst)
print(t)
