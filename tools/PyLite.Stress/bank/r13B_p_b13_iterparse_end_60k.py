# deadline: 60000
# iterparse, end events only, over a 60,000-item feed (no clear): the common "sum a field" loop
import xml.etree.ElementTree as ET
import io, time
N = 60000
doc = '<feed>' + ''.join('<item sku="S%d"><name>Item %d</name><qty>%d</qty><tags><t>a</t><t>b</t></tags></item>' % (i, i, i % 9) for i in range(N)) + '</feed>'
t0 = time.perf_counter()
tot = 0
n = 0
for ev, el in ET.iterparse(io.StringIO(doc)):
    n += 1
    if el.tag == 'qty':
        tot += int(el.text)
print(n, tot, round(time.perf_counter() - t0, 3))
