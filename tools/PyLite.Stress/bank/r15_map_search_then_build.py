# deadline: 30000
import xml.etree.ElementTree as ET, time
small = ET.fromstring('<a><b><c/></b></a>')
print(len(small.findall('.//c/..')))
t0 = time.perf_counter()
doc = ET.Element('d')
for i in range(40000):
    r = ET.SubElement(doc, 'rec')
    ET.SubElement(r, 'f').text = str(i)
    r.set('k', '1')
print(len(doc), round((time.perf_counter() - t0) * 1000))
print(len(small.findall('.//c/..')))
