# deadline: 30000
import xml.etree.ElementTree as ET, copy
doc = ET.Element("doc")
draft = ET.SubElement(doc, "draft")
table = ET.SubElement(doc, "table")
other = ET.Element("other")
first = ET.SubElement(draft, "row", n="-1")
bad = 0
for i in range(6000):
    row = ET.SubElement(table, "row", n="r%d" % i)
    if table.find("row[last()]/..") is not table:
        bad += 1
print(bad)
