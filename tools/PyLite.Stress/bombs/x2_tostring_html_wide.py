# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(500000):
    ET.SubElement(r, 'br')
print(len(ET.tostring(r, encoding='unicode', method='html')))
