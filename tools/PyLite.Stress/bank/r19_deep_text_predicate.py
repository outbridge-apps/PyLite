# Review 13, row 24: default limits, 300 records each a 60-deep chain, 1,500 lookups with a text predicate (before the fix: 256 MB refused; 1.1.0 and now: 2 MB).
import xml.etree.ElementTree as ET
root = ET.Element('r')
for i in range(300):
    n = ET.SubElement(root, 'row')
    for d in range(60):
        n = ET.SubElement(n, 'c')
    n.text = str(i)
n = 0
for k in range(1500):
    n += len(root.findall("row[.='7']"))
print(n)
