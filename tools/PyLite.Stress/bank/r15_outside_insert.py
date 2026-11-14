# deadline: 30000
import xml.etree.ElementTree as ET, copy
doc = ET.Element("doc")
draft = ET.SubElement(doc, "draft")
table = ET.SubElement(doc, "table")
other = ET.Element("other")
first = ET.SubElement(draft, "row", n="-1")
bad = 0
for i in range(20000):
    ET.SubElement(table, "row")
for i in range(2000):
    del other[:]
    other.insert(0, ET.Element("y"))
    if doc.find("table/row[1]/..") is not table:
        bad += 1
print(bad)
