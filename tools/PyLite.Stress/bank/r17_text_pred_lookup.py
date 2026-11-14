# deadline: 60000
# allocmb: 64
# Review 12, row 12: a lookup by a child's text, 4,000 times over 1,500 rows. No charge per candidate.
import xml.etree.ElementTree as ET
root = ET.Element("R")
for i in range(1500):
    r = ET.SubElement(root, "row", id=str(i))
    ET.SubElement(r, "v").text = str(i)
n = 0
for i in range(4000):
    n += len(root.findall("row[v='%d']" % (i % 1500)))
print(n)
