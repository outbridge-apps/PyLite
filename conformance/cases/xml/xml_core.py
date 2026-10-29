# Adapted from CPython's test suite (Lib/test/test_xml_etree.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# note: the dialect resolves the full dotted module name only
# All the CPython import forms work: "from xml.etree import ElementTree", "import xml.etree.ElementTree"
import xml.etree.ElementTree as ET

# parse a small document
root = ET.fromstring('<root><item id="1">first</item><item id="2">second</item></root>')
assert root.tag == 'root'
assert len(root) == 2
assert root[0].tag == 'item'
assert root[0].text == 'first'
assert root[1].text == 'second'

# attributes
assert root[0].get('id') == '1'
assert root[1].attrib == {'id': '2'}

# iteration over children
texts = [child.text for child in root]
assert texts == ['first', 'second']

# nested structure
doc = ET.fromstring('<a><b><c>deep</c></b></a>')
assert doc[0][0].text == 'deep'
assert doc[0][0].tag == 'c'

# building a tree programmatically and serializing
built = ET.Element('row')
cell = ET.SubElement(built, 'cell')
cell.text = 'v'
out = ET.tostring(built)
s = out.decode() if isinstance(out, bytes) else out
assert '<row>' in s and '<cell>v</cell>' in s

# round trip
again = ET.fromstring(s)
assert again.tag == 'row'
assert again[0].text == 'v'

# iselement
assert ET.iselement(root)
assert not ET.iselement('no')
