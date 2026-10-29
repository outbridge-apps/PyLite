# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET, copy
r = ET.Element('r'); e = r
for i in range(200000):
    e = ET.SubElement(e, 'd')
print(len(copy.deepcopy(r)))
