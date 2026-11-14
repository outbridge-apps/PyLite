# deadline: 60000
# iterparse start+end over the same feed, no clear
import xml.etree.ElementTree as ET
import io, time
N = 60000
doc = '<feed>' + ''.join('<item sku="S%d"><name>Item %d</name><qty>%d</qty><tags><t>a</t><t>b</t></tags></item>' % (i, i, i % 9) for i in range(N)) + '</feed>'
t0 = time.perf_counter()
d = 0
n = 0
for ev, el in ET.iterparse(io.StringIO(doc), events=('start', 'end')):
    n += 1
    d = d + 1 if ev == 'start' else d - 1
print(n, d, round(time.perf_counter() - t0, 3))
