# the "first match" idiom: next(rec.iterfind(path), None) per record, iterators dropped unfinished
import xml.etree.ElementTree as ET
import time
root = ET.fromstring('<d>' + ''.join('<rec><f>%d</f><f>%d</f><g>x</g><f>9</f></rec>' % (i, i + 1) for i in range(20000)) + '</d>')
t0 = time.perf_counter()
s = 0
for k in range(5):
    for rec in root:
        e = next(rec.iterfind('f[2]'), None)
        s += int(e.text)
print(s, round(time.perf_counter() - t0, 3))
