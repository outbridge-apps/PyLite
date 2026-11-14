import xml.etree.ElementTree as ET, time
t0 = time.perf_counter()
doc = ET.Element('doc')
for i in range(6000):
    ET.SubElement(doc, 'row', n=str(i))
    if 'iterlist' == 'find':
        last = doc.find('row[last()]')
    elif 'iterlist' == 'iternext':
        last = next(doc.iterfind('row[last()]'))
    else:
        last = list(doc.iterfind('row[last()]'))[0]
print('iterlist', 6000, round((time.perf_counter() - t0) * 1000), last.get('n'))
