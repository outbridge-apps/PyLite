import xml.etree.ElementTree as ET

doc = '''<orders date="2026-10-29">
  <order id="1" status="open"><item sku="A1" qty="2">Apple</item><item sku="B2" qty="1">Pear</item></order>
  <order id="2" status="closed"><item sku="A1" qty="5">Apple</item>tail-text</order>
  <note>first<b>bold</b>after</note>
</orders>'''
root = ET.fromstring(doc)
out = []
out.append((root.tag, root.get('date'), root.get('missing', 'dflt'), len(root)))
out.append([o.get('id') for o in root.findall('order')])
out.append([i.text for i in root.iter('item')])
out.append([i.get('sku') for i in root.findall('.//item')])
out.append([o.get('id') for o in root.findall("order[@status='closed']")])
out.append([i.text for i in root.findall('.//item[@qty]')])
out.append(root.find('order/item').attrib == {'sku': 'A1', 'qty': '2'})
out.append(root.findtext('note'))
out.append(root.findtext('nothing', 'none'))
note = root.find('note')
out.append((note.text, note[0].text, note[0].tail))
out.append(root.find('order[2]').get('id'))
out.append(root.find('order').find('item').tail)
out.append(sum(int(i.get('qty')) for i in root.iter('item')))
out.append([e.tag for e in root.iter()][:4])
out.append(''.join(note.itertext()))
result = out
