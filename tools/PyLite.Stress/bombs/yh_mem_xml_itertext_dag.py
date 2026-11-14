# deadline: 3000
# allocmb: 64
import xml.etree.ElementTree as ET
R = ET.Element('r')
A = ET.Element('a')
B = ET.Element('b')
B.text = 'b'
A.extend([B] * 4000)
R.extend([A] * 4000)
for t in R.itertext():
    pass
