# deadline: 60000
# ERP export: order lines with six attributes, parsed and totalled
import xml.etree.ElementTree as ET
import time
N = 100000
parts = ['<orders>']
for i in range(N):
    parts.append('<line id="%d" item="A-%05d" qty="%d" price="%d.50" cur="USD" wh="W%d"/>' % (i, i % 99999, i % 7 + 1, i % 300, i % 4))
parts.append('</orders>')
doc = ''.join(parts)
parts = None
t0 = time.perf_counter()
root = ET.fromstring(doc)
t1 = time.perf_counter()
tot = 0
for ln in root:
    tot += int(ln.get('qty')) * float(ln.get('price'))
t2 = time.perf_counter()
print(len(doc), len(root), round(tot, 2), 'parse', round(t1 - t0, 3), 'sum', round(t2 - t1, 3))
