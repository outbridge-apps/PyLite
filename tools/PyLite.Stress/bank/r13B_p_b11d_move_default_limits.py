# default limits: one row moved once, then 6,000 rounds of add-a-row + find row[last()]
import xml.etree.ElementTree as ET
import time
doc = ET.Element('doc')
draft = ET.SubElement(doc, 'draft')
table = ET.SubElement(doc, 'table')
first = ET.SubElement(draft, 'row', n='-1')
draft.remove(first)
table.append(first)
t0 = time.perf_counter()
for i in range(6000):
    row = ET.SubElement(table, 'row', n=str(i))
    ET.SubElement(row, 'cell').text = str(i)
    if table.find('row[last()]') is not row:
        raise SystemExit('wrong')
print(len(table), round(time.perf_counter() - t0, 3))
