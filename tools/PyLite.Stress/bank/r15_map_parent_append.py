# deadline: 30000
import xml.etree.ElementTree as ET, time
t0 = time.perf_counter()
doc = ET.Element('doc')
s = 0
for i in range(3000):
    row = ET.SubElement(doc, 'row', n=str(i))
    ET.SubElement(row, 'f')
    s += len(doc.findall('.//f/..'))
print(s, round((time.perf_counter() - t0) * 1000))
