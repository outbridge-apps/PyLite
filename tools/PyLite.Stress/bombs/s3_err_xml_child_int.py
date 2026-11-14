# deadline: 3000
# allocmb: 256
import xml.etree.ElementTree as ET
e = ET.Element('r')
ET.SubElement(e, 'a')
try:
    e[0] = 5
except TypeError as x:
    print('clean', type(x).__name__)
