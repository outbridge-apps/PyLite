# deadline: 3000
# allocmb: 64
import xml.etree.ElementTree as ET
x = ET.tostring(ET.Element('a', b=chr(34) * 3000000))
