from xml.etree import ElementTree as ET
import time
row = '<row>' + ''.join('<c%d><v>%d</v></c%d>' % (j, j, j) for j in range(15)) + '</row>'
r = ET.fromstring('<table>' + row * 2000 + '</table>')
# an ordinary reorder of the rows: every row placed a second time
t2 = ET.Element('table')
t2.extend(sorted(r, key=lambda e: e.findtext('c0/v')))
r = t2
t = time.perf_counter()
n = 0
for _ in range(80):
    n += len(r.findall('row[1]'))
    n += len(r.findall('row[last()]'))
print(n, round(time.perf_counter() - t, 3))
