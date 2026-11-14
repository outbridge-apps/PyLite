# deadline: 60000
# invoices: first/last line per invoice, positional with attribute filters, whole-tree positional repeated
import xml.etree.ElementTree as ET
import time
root = ET.Element('invoices')
for i in range(3000):
    inv = ET.SubElement(root, 'invoice', no=str(i))
    ET.SubElement(inv, 'head').text = 'H%d' % i
    for j in range(20):
        ln = ET.SubElement(inv, 'line', n=str(j))
        if j % 3 == 0:
            ln.set('tax', 'Y')
        ln.text = str(i + j)
t0 = time.perf_counter()
a = b = c = d = 0
for inv in root:
    a += int(inv.find('line[1]').text)
    b += int(inv.find('line[last()]').text)
    c += len(inv.findall("line[@tax][1]"))
    d += int(inv.findtext("line[@tax='Y'][1]"))
t1 = time.perf_counter()
e = 0
for k in range(20):
    e += len(root.findall('.//line[1]'))
    e += len(root.findall('invoice/line[@tax][last()]'))
t2 = time.perf_counter()
print(a, b, c, d, e, 'per-invoice', round(t1 - t0, 3), 'tree', round(t2 - t1, 3))
