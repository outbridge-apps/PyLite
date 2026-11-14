# deadline: 3000
# allocmb: 256
import xml.etree.ElementTree as ET
e = ET.Element('r')
c = ET.SubElement(e, 'c')
c.tail = 5
try:
    e.findall(".[.='z']")
except TypeError as x:
    print('clean', type(x).__name__)
