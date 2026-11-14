# deadline: 30000
# allocmb: 64
import xml.etree.ElementTree as ET
r = ET.Element("r")
for i in range(2000):
    g = ET.SubElement(r, "g")
    for j in range(20):
        ET.SubElement(g, "c", a="1").text = "x"
its = []
k = 0
while True:
    it = r.iterfind('.//*[2]')
    next(it, None)
    its.append(it)
    k += 1
    if k % 50 == 0:
        print(k)
