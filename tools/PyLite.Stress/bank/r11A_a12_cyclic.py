import xml.etree.ElementTree as ET, copy
a = ET.Element('a')
a.append(a)
print([e is a for e in a.findall('*[1]')], [e is a for e in a.findall('a[last()]')])
b = ET.Element('b'); x = ET.SubElement(b, 'x'); x.append(b)
print([e.tag for e in b.findall('x/b/x[1]')])
try:
    print(len(a.findall('.//a[1]')))
except BaseException as ex:
    print('desc', type(ex).__name__)
