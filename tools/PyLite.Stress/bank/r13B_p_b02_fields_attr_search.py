# deadline: 60000
# records of <f n=.. v=..> fields, mapped with attribute predicates
import xml.etree.ElementTree as ET
import time
N = 20000
names = ['id', 'item', 'qty', 'price', 'cur', 'wh', 'note', 'date']
parts = ['<doc>']
for i in range(N):
    parts.append('<rec no="%d">' % i)
    for j, n in enumerate(names):
        parts.append('<f n="%s" v="%d"/>' % (n, i * 8 + j))
    parts.append('</rec>')
parts.append('</doc>')
doc = ''.join(parts)
parts = None
t0 = time.perf_counter()
root = ET.fromstring(doc)
t1 = time.perf_counter()
tot = 0
for rec in root.findall('rec'):
    q = rec.find("f[@n='qty']")
    p = rec.find("f[@n='price']")
    tot += int(q.get('v')) + int(p.get('v'))
t2 = time.perf_counter()
c1 = len(root.findall(".//f[@n='date']"))
c2 = len(root.findall(".//rec[@no]"))
c3 = len(root.findall("rec/f[1]"))
c4 = len(root.findall("rec/f[last()]"))
t3 = time.perf_counter()
print(len(root), tot, c1, c2, c3, c4, 'parse', round(t1 - t0, 3), 'map', round(t2 - t1, 3), 'all', round(t3 - t2, 3))
