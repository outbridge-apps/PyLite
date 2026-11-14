# repeat: 2
# deepcopy a template per record, sort lines by price, read positional children, serialise and re-parse
import xml.etree.ElementTree as ET
import copy
import time
tpl = ET.fromstring('<invoice><head><no/><date/></head><lines/><total cur="EUR"/></invoice>')
root = ET.Element('batch')
t0 = time.perf_counter()
for i in range(2500):
    inv = copy.deepcopy(tpl)
    inv.find('head/no').text = str(i)
    inv.find('head/date').text = '2026-11-%02d' % (i % 28 + 1)
    lines = inv.find('lines')
    for j in range(12):
        ET.SubElement(lines, 'line', price=str((i * 31 + j * 17) % 997), qty=str(j + 1))
    lines[:] = sorted(lines, key=lambda e: int(e.get('price')))
    inv.find('total').text = str(sum(int(e.get('price')) * int(e.get('qty')) for e in lines))
    root.append(inv)
t1 = time.perf_counter()
cheap = 0
dear = 0
for inv in root:
    cheap += int(inv.find('lines/line[1]').get('price'))
    dear += int(inv.find('lines/line[last()]').get('price'))
t2 = time.perf_counter()
s = ET.tostring(root, encoding='unicode')
back = ET.fromstring(s)
n = len(back.findall('.//line'))
top = [e.get('price') for e in back.findall('invoice/lines/line[1]')][:5]
t3 = time.perf_counter()
print(cheap, dear, len(s), n, top, 'build', round(t1 - t0, 3), 'read', round(t2 - t1, 3), 'ser', round(t3 - t2, 3))
