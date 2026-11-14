import xml.etree.ElementTree as ET, io
doc = '<root>' + ''.join('<record id="%d"><v>%d</v></record>' % (i, i) for i in range(10)) + '</root>'
context = ET.iterparse(io.StringIO(doc), events=('start', 'end'))
_, root = next(context)
seen = []
for ev, el in context:
    if ev == 'end' and el.tag == 'record':
        seen.append(el.get('id') + ':' + el.findtext('v', ''))
        root.clear()
print('start/end + root.clear():', len(seen), seen)
seen = []
for ev, el in ET.iterparse(io.StringIO(doc)):
    if el.tag == 'record':
        seen.append(el.findtext('v'))
        el.clear()
print('end + el.clear():', len(seen), seen)
