# allocmb: 64
# deadline: 30000
import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(100000):
    ET.SubElement(r, 'c')
its = []
for i in range(3000):
    r.insert(0, ET.Element("c"))
    it = r.iterfind('c[1]')
    next(it)
    its.append(it)
print(len(its))
