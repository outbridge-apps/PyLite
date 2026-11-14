# deadline: 60000
import time
from xml.etree import ElementTree as ET
T = time.perf_counter
row = "<row>" + "".join("<c%d><v>%d</v></c%d>" % (j, j, j) for j in range(15)) + "</row>"
r = ET.fromstring("<table>" + row * 2000 + "</table>")
t = T()
n = 0
for _ in range(20):
    n += len(r.findall("row[1]"))
    n += len(r.findall("row[last()]"))
print(n, "pos_ms=%d" % ((T() - t) * 1000))
