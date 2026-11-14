import xml.etree.ElementTree as ET, time
t0 = time.perf_counter()
doc = ET.Element('doc')
for i in range(3000):
    ET.SubElement(doc, 'row', n=str(i))
    if 'iternext' == 'find':
        last = doc.find('row[last()]')
    elif 'iternext' == 'iternext':
        last = next(doc.iterfind('row[last()]'))
    else:
        last = list(doc.iterfind('row[last()]'))[0]
print('iternext', 3000, round((time.perf_counter() - t0) * 1000), last.get('n'))
