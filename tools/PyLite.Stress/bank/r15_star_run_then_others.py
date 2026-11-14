# deadline: 30000
import xml.etree.ElementTree as ET
root = ET.Element('root')
for i in range(12000):
    ET.SubElement(root, 'a')
for i in range(12000):
    ET.SubElement(root, 't%d' % i)
print(len(root.findall('*[1]')), len(root.findall('*[last()]')), len(list(root.iterfind('*[2]'))))
