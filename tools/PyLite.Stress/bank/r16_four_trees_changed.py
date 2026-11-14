# deadline: 30000
import xml.etree.ElementTree as ET
N = 60000
docs = []
for d in range(4):
    r = ET.Element('r')
    for i in range(N): ET.SubElement(r, 'x')
    docs.append(r)
n = 0
for rnd in range(3):
    for r in docs:
        del r[0]                       # a change in this tree only
        if r.find('x/..') is r: n += 1
print(n)
