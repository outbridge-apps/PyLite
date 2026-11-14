# deadline: 60000
import xml.etree.ElementTree as ET
import time
parts = ['<r>']
for i in range(3000):
    parts.append('<p id="%d"><n>a%d</n><n>b</n><q k="1"/><q/></p>' % (i, i))
parts.append('</r>')
root = ET.fromstring(''.join(parts))
def tm(label, f, rep):
    t0 = time.perf_counter()
    try:
        for _ in range(rep):
            r = f()
    except Exception as e:
        print(label, "EXC", e); return
    print(label, round((time.perf_counter() - t0) * 1000), "ms", len(r) if isinstance(r, list) else r)
tm('findall p/n[1]', lambda: root.findall('p/n[1]'), 20)
tm('findall .//n[last()]', lambda: root.findall('.//n[last()]'), 20)
tm('findall p[@id]', lambda: root.findall('p[@id]'), 20)
tm('findall .//q[@k]', lambda: root.findall('.//q[@k]'), 20)
tm('find p[@id="2999"]', lambda: root.find('p[@id="2999"]').get('id'), 20)
tm('findall p[last()]', lambda: root.findall('p[last()]'), 200)
tm('findall p[1]/n', lambda: root.findall('p[1]/n'), 200)
tm('findall .//n/..', lambda: root.findall('.//n/..'), 10)
tm('iterfind p/n[2]', lambda: list(root.iterfind('p/n[2]')), 20)
tm('per-p find n[1]', lambda: [p.find('n[1]') for p in root], 10)
tm('per-p findtext n[2]', lambda: [p.findtext('n[2]') for p in root], 10)
tm('per-p get', lambda: [p.get('id') for p in root], 50)
tm('attrib items', lambda: [list(p.attrib.items()) for p in root], 20)
tm('tostring', lambda: len(ET.tostring(root)), 5)
t0 = time.perf_counter()
doc = ET.Element('doc')
for i in range(3000):
    ET.SubElement(doc, 'row', n=str(i))
    last = doc.findall('row[last()]')[0]
print('add+last', round((time.perf_counter() - t0) * 1000), last.get('n'))
t0 = time.perf_counter()
doc = ET.Element('doc')
for i in range(1500):
    ET.SubElement(doc, 'row', n=str(i))
    last = next(doc.iterfind('row[last()]'))
print('add+iterfind last', round((time.perf_counter() - t0) * 1000), last.get('n'))
