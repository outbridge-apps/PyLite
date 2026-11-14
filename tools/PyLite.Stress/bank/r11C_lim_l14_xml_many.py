# allocmb: 64
# deadline: 20000
import xml.etree.ElementTree as ET
r = ET.Element("r")
for i in range(1000000):
    ET.SubElement(r, "c")
print(len(r))
