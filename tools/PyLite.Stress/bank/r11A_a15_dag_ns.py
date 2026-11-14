# deadline: 1000
import xml.etree.ElementTree as ET
e = ET.Element('x')
for i in range(60):
    p = ET.Element('x')
    p.extend([e, e, e, e])
    e = p
k = 'ns'
if k == 'iter':
    print(sum(1 for _ in e.iter('zz')))
elif k == 'find':
    print(e.find('.//zz'))
elif k == 'map':
    print(len(e.findall('.//x[2]')))
elif k == 'ns':
    print(len(ET.tostring(e)))
else:
    print(len(e.findall(".//x[.='q']")))
