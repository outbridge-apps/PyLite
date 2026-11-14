# deadline: 15000
import xml.etree.ElementTree as ET
r = ET.Element('r')
n = 0
try:
    for i in range(400):
        p = ET.SubElement(r, 'p')
        for j in range(5000):
            ET.SubElement(p, 'c')
            n += 1
except MemoryError:
    pass
print(n)
