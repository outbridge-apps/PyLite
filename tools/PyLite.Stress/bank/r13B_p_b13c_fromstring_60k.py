# deadline: 60000
# the same feed through fromstring + iter (reference)
import xml.etree.ElementTree as ET
import time
N = 60000
doc = '<feed>' + ''.join('<item sku="S%d"><name>Item %d</name><qty>%d</qty><tags><t>a</t><t>b</t></tags></item>' % (i, i, i % 9) for i in range(N)) + '</feed>'
t0 = time.perf_counter()
root = ET.fromstring(doc)
tot = sum(int(e.text) for e in root.iter('qty'))
print(tot, round(time.perf_counter() - t0, 3))
