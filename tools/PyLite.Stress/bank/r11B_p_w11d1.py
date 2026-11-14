import time
import xml.etree.ElementTree as ET
t = time.perf_counter()
root = ET.Element("Orders")
for i in range(4000):
    o = ET.SubElement(root, "Order", id=str(i), status="open" if i % 3 else "closed")
    ET.SubElement(o, "Customer").text = "C%04d" % (i % 300)
    lines = ET.SubElement(o, "Lines")
    for j in range(4):
        ln = ET.SubElement(lines, "Line", no=str(j))
        ET.SubElement(ln, "Item").text = "ITEM-%d" % (j * 7 + i % 11)
        ET.SubElement(ln, "Qty").text = str(j + 1)
        ET.SubElement(ln, "Price").text = "%d.%02d" % (i % 90 + 1, j * 13 % 100)
t1 = time.perf_counter()
s = ET.tostring(root, encoding="unicode")
t2 = time.perf_counter()
doc = ET.fromstring(s)
t3 = time.perf_counter()
total = 0.0
for o in doc.findall("Order[@status='open']"):
    for ln in o.iterfind("Lines/Line"):
        total += int(ln.findtext("Qty")) * float(ln.findtext("Price"))
print(total)
