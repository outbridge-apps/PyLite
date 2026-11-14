# deadline: 30000
# the "first match" idiom: next(rec.iterfind(path), None) per record, iterators dropped unfinished
import xml.etree.ElementTree as ET
root = ET.fromstring('<d>' + ''.join('<rec><f>%d</f><f>%d</f><g>x</g><f>9</f></rec>' % (i, i + 1) for i in range(20000)) + '</d>')
s = 0
for k in range(12):
    for rec in root:
        e = next(rec.iterfind('f[2]'), None)
        s += int(e.text)
print(s)
