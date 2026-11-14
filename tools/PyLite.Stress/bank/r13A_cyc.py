import xml.etree.ElementTree as ET
r = ET.Element('r'); y = ET.SubElement(r, 'y'); y.append(r)
print('built')
x = r.findall('y/r')
print('plain', len(x))
x = r.findall('y/r[1]')
print('pos', len(x))
