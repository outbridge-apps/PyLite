# deadline: 3000
# allocmb: 256
import xml.etree.ElementTree as ET
r = ET.Element('r')
L = [ET.Element('a')] * 4000000
for i in range(10):
    r.extend(L)
