import xml.etree.ElementTree as ET
e = ET.Element('e', {('a%d' % i): 'v' for i in range(2000)})
root = ET.Element('r')
root.extend([e] * 500000)
print('built')
s = ET.tostring(root)
print(len(s))
