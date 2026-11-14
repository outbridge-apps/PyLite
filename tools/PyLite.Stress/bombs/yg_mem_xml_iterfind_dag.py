# deadline: 3000
# allocmb: 64
import xml.etree.ElementTree as ET
R = ET.Element('r')
A = ET.Element('a')
A.extend([ET.Element('b')] * 4000)
R.extend([A] * 4000)
for e in R.iterfind('*/*'):
    pass
