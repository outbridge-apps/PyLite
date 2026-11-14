# deadline: 60000
# streaming idiom over a big feed: start+end events, clear processed records
import xml.etree.ElementTree as ET
import io
import time
N = 20000
parts = ['<feed>']
for i in range(N):
    parts.append('<item sku="S%d"><name>Item %d</name><qty>%d</qty><tags><t>a</t><t>b</t></tags></item>' % (i, i, i % 9))
parts.append('</feed>')
doc = ''.join(parts)
parts = None
t0 = time.perf_counter()
tot = 0
cnt = 0
depth = 0
root = None
for ev, el in ET.iterparse(io.StringIO(doc), events=('start', 'end')):
    if ev == 'start':
        if root is None:
            root = el
        depth += 1
        continue
    depth -= 1
    if el.tag == 'item':
        tot += int(el.findtext('qty'))
        cnt += 1
        root.clear()
t1 = time.perf_counter()
tot2 = 0
for ev, el in ET.iterparse(io.StringIO(doc)):
    if el.tag == 'qty':
        tot2 += int(el.text)
t2 = time.perf_counter()
print(cnt, tot, tot2, 'se', round(t1 - t0, 3), 'end', round(t2 - t1, 3))
