# deadline: 60000
# a parsed 40,000-record document, whole-tree positional searches: what do they add to the charge?
import xml.etree.ElementTree as ET
import time
N = 40000
doc = '<doc>' + ''.join('<rec no="%d"><f>a</f><f>b</f><g>c</g><f>d</f><h k="1">e</h><f>x</f></rec>' % i for i in range(N)) + '</doc>'
root = ET.fromstring(doc)
doc = None
t0 = time.perf_counter()
r = []
r.append(len(root.findall('.//f[1]')))
r.append(len(root.findall('rec/f[2]')))
r.append(len(root.findall('.//rec/*[3]')))
r.append(len(root.findall(".//h[@k='1'][1]")))
r.append(len(root.findall('rec/f[last()]')))
r.append(len(list(root.iterfind('.//f[2]'))))
t1 = time.perf_counter()
print(r, round(t1 - t0, 3))
