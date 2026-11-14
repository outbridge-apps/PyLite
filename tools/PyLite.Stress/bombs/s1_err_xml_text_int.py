# deadline: 3000
# allocmb: 256
import xml.etree.ElementTree as ET
e = ET.Element('r')
e.text = 5
try:
    ET.tostring(e)
except TypeError as x:
    print('clean', type(x).__name__)
