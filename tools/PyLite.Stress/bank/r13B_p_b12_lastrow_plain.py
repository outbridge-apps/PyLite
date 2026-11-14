# deadline: 60000
# rows built in a draft section, the first row moved to the final table (remove + append), then the
# add-a-row + find row[last()] loop
import xml.etree.ElementTree as ET
import time
doc = ET.Element('doc')
draft = ET.SubElement(doc, 'draft')
table = ET.SubElement(doc, 'table')



t0 = time.perf_counter()
N = 6000
for i in range(N):
    row = ET.SubElement(table, 'row', n=str(i))
    ET.SubElement(row, 'cell').text = str(i)
    if table.find('row[last()]') is not row:
        raise SystemExit('wrong')
t1 = time.perf_counter()
print(len(table), round(t1 - t0, 3))
