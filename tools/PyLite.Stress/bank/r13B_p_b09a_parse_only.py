# deadline: 60000
# a parsed 40,000-record document, whole-tree positional searches: what do they add to the charge?
import xml.etree.ElementTree as ET
import time
N = 40000
doc = '<doc>' + ''.join('<rec no="%d"><f>a</f><f>b</f><g>c</g><f>d</f><h k="1">e</h><f>x</f></rec>' % i for i in range(N)) + '</doc>'
root = ET.fromstring(doc)
doc = None
t0 = time.perf_counter()
r=[]
t1 = time.perf_counter()
print(len(root), round(t1 - t0, 3))
