# repeat: 2
# deadline: 60000
import time
from xml.etree import ElementTree as ET
T = time.perf_counter
items = "".join("<item id='%d' kind='k%d'><sub a='1'>%d</sub><name>n%d</name></item>" % (i, i % 5, i, i) for i in range(20000))
r = ET.fromstring("<root>" + items + "</root>")
out = []
t = T()
for _ in range(5):
    n = sum(1 for _ in r.iter("sub"))
out.append("iter=%d" % ((T() - t) * 1000))
t = T()
for _ in range(5):
    n = len(r.findall("item[2]"))
out.append("pos=%d" % ((T() - t) * 1000))
t = T()
for _ in range(3):
    n = len(ET.tostring(r))
out.append("tostring=%d" % ((T() - t) * 1000))
t = T()
for _ in range(3):
    n = len(list(r.iter()))
out.append("iterall=%d" % ((T() - t) * 1000))
print(" ".join(out))
