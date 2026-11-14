# deadline: 60000
# a report: a small header sorted once (slice assignment), then the add-a-row + find row[last()] loop
import xml.etree.ElementTree as ET
import time
hdr = ET.Element('header')
for k in ['zeta', 'alpha', 'mid']:
    ET.SubElement(hdr, 'col', name=k)
hdr[:] = sorted(hdr, key=lambda e: e.get('name'))
table = ET.Element('table')
table.append(hdr)
t0 = time.perf_counter()
N = 6000
for i in range(N):
    row = ET.SubElement(table, 'row', n=str(i))
    ET.SubElement(row, 'cell').text = str(i)
    last = table.find('row[last()]')
    if last is not row:
        raise SystemExit('wrong')
t1 = time.perf_counter()
print(len(table), [c.get('name') for c in hdr], round(t1 - t0, 3))
