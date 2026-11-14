# deadline: 60000
# Review 12, row 15: itertext() of each row, 300,000 walks of a small element.
import xml.etree.ElementTree as ET
root = ET.Element("R")
for i in range(1500):
    r = ET.SubElement(root, "row", id=str(i))
    ET.SubElement(r, "v").text = str(i)
n = 0
for k in range(200):
    for r in root:
        n += len(''.join(r.itertext()))
print(n)
