import xml.etree.ElementTree as ET
r = ET.Element('r')
for i in range(400):
    p = ET.SubElement(r, 'p')
    for j in range(5000):
        ET.SubElement(p, 'c')
print(sum(len(p) for p in r))
