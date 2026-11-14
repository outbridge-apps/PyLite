# items: 1000
# deadline: 3000
# allocmb: 64
import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(3000):
    ET.SubElement(r, 'a')
