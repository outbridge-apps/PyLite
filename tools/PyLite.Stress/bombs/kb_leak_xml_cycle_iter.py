# deadline: 3000
# allocmb: 1024
import xml.etree.ElementTree as ET
a = ET.Element('a')
a.append(a)
for x in a.iter('zzz'):
    pass
