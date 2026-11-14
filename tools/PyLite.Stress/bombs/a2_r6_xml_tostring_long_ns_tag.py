# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
t = '{' + 'u' * 4000000 + '}a'
root = ET.Element(t)
for _ in range(5):
    ET.SubElement(root, t)
print(len(ET.tostring(root)))
