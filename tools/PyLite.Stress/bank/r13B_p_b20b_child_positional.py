# deadline: 60000
# a parsed document of 30,000 invoices x 6 lines; whole-tree positional searches repeated (single predicates)
import xml.etree.ElementTree as ET
import time
doc = '<invoices>' + ''.join('<invoice no="%d"><head>h</head>%s</invoice>' % (i, ''.join('<line n="%d">%d</line>' % (j, i + j) for j in range(6))) for i in range(30000)) + '</invoices>'
root = ET.fromstring(doc)
doc = None
t0 = time.perf_counter()
e = 0
for k in range(10):
    e += len(root.findall('invoice/line[1]'))
    e += len(root.findall('invoice/line[last()]'))
    e += len(root.findall('invoice/*[2]'))
    e += sum(1 for _ in root.iterfind('invoice/line[2]'))
print(e, round(time.perf_counter() - t0, 3))
