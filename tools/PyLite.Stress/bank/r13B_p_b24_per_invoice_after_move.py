# per-invoice first/last line, after one element was moved between invoices (remove + append)
import xml.etree.ElementTree as ET
import time
root = ET.Element('invoices')
for i in range(3000):
    inv = ET.SubElement(root, 'invoice', no=str(i))
    for j in range(20):
        ET.SubElement(inv, 'line', n=str(j)).text = str(i + j)
x = root[0][0]
root[0].remove(x)
root[1].append(x)
t0 = time.perf_counter()
a = b = 0
for k in range(3):
    for inv in root:
        a += int(inv.find('line[1]').text)
        b += int(inv.find('line[last()]').text)
print(a, b, round(time.perf_counter() - t0, 3))
