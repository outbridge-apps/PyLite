import xml.etree.ElementTree as ET


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


out = []
root = ET.Element('invoice')
root.set('no', '42')
line = ET.SubElement(root, 'line')
line.text = 'Tea & "Biscuits" <x>'
ET.SubElement(root, 'empty')
total = ET.SubElement(root, 'total')
total.text = '9.50'
total.tail = '\n'
out.append(ET.tostring(root, encoding='unicode'))
out.append(ET.tostring(ET.Element('a'), encoding='unicode'))
e = ET.fromstring('<a><b/><c>x</c></a>')
e.remove(e.find('b'))
e.append(ET.Element('d'))
e.insert(0, ET.Element('first'))
out.append([c.tag for c in e])
out.append(ET.tostring(e, encoding='unicode'))
ns = ET.fromstring('<r xmlns:p="urn:x"><p:a>1</p:a><a>2</a></r>')
out.append([c.tag for c in ns])
out.append(ns.find('{urn:x}a').text)
out.append(ns.find('p:a', {'p': 'urn:x'}).text)
out.append(err(lambda: ET.fromstring('<a><b></a>')))
out.append(err(lambda: ET.fromstring('')))
out.append(err(lambda: ET.fromstring('<a>&nope;</a>')))
out.append(ET.fromstring('<a>&amp;&lt;&#65;</a>').text)
result = out
