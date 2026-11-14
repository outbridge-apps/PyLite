import xml.etree.ElementTree as ET, time
t0 = time.perf_counter()
doc = ET.Element('doc')
for i in range(6000):
    ET.SubElement(doc, 'row', n=str(i))
    if 'find' == 'find':
        last = doc.find('row[last()]')
    elif 'find' == 'iternext':
        last = next(doc.iterfind('row[last()]'))
    else:
        last = list(doc.iterfind('row[last()]'))[0]
print('find', 6000, round((time.perf_counter() - t0) * 1000), last.get('n'))
