# Review 13, row 24: itertext() and iter() of a 40-deep element, drained 20,000 times; a drained walk is given back (before the fix: 32 MB; now 17; 1.1.0: 7).
import xml.etree.ElementTree as ET
root = ET.Element('r'); n = root
for d in range(40):
    n = ET.SubElement(n, 'c'); n.text = 't'
c = 0
for k in range(20000):
    c += len(''.join(root.itertext())) + sum(1 for e in root.iter())
print(c)
