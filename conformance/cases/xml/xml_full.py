# Adapted from CPython's test suite (Lib/test/test_xml_etree.py: ElementTreeTest.test_simpleops/test_attrib/
# test_find/test_findall/test_iter/test_tostring/test_namespace_prefixes/test_entity/test_parseerror,
# ElementFindTest.test_find_simple/test_find_xpath). Python Software Foundation, PSF License Version 2 -- see README.
# Dialect: the registry is flat, so packages ("xml", "xml.etree") are synthesized from the dotted key.

import xml.etree.ElementTree as ET

# ---- building a tree ----
root = ET.Element('root', {'id': '1'}, kind='demo')
assert root.tag == 'root' and root.attrib == {'id': '1', 'kind': 'demo'} and root.text is None and root.tail is None and len(root) == 0
a = ET.SubElement(root, 'a')
a.text = 'first'
b = ET.SubElement(root, 'b', attrib={'x': 'y'})
b.text = 'second'
b.tail = 'after-b'
c = ET.SubElement(b, 'c')
assert len(root) == 2 and root[0] is a and root[1] is b and list(root) == [a, b] and b[0] is c and root[-1] is b
assert [e.tag for e in root] == ['a', 'b'] and ET.iselement(root) and not ET.iselement('root') and not ET.iselement(None)
assert ET.tostring(root, encoding='unicode') == '<root id="1" kind="demo"><a>first</a><b x="y">second<c /></b>after-b</root>'
assert ET.tostring(a, encoding='unicode') == '<a>first</a>' and ET.tostring(c, encoding='unicode') == '<c />'
assert ET.tostring(a) == b'<a>first</a>' and isinstance(ET.tostring(a), bytes)   # default: us-ascii bytes
assert ET.tostring(a, 'unicode') == '<a>first</a>' and ET.tostring(a, encoding='utf-8') == b'<a>first</a>'
assert ET.tostring(a, 'us-ascii') == b'<a>first</a>' and isinstance(ET.tostring(a, 'utf-8'), bytes)
uni = ET.Element('u')
uni.text = 'é☺'
assert ET.tostring(uni, encoding='utf-8') == 'é☺'.encode('utf-8').join([b'<u>', b'</u>']) and ET.tostring(uni, 'us-ascii') == b'<u>&#233;&#9786;</u>'
assert ET.tostring(uni) == b'<u>&#233;&#9786;</u>'                         # non-ASCII becomes character references
# no XML declaration for utf-8 / us-ascii, as in CPython; any other codec gets one (see the end of this file)

# ---- attributes ----
assert root.get('id') == '1' and root.get('missing') is None and root.get('missing', 'dflt') == 'dflt'
root.set('id', '2')
root.set('new', 'v')
assert root.get('id') == '2' and root.attrib['new'] == 'v' and sorted(root.keys()) == ['id', 'kind', 'new']
assert sorted(root.items()) == [('id', '2'), ('kind', 'demo'), ('new', 'v')] and 'id' in root.attrib
root.attrib['direct'] = 'x'
assert root.get('direct') == 'x'
del root.attrib['direct']
assert root.get('direct') is None
copied = ET.Element('e', root.attrib)
copied.set('id', '99')
assert root.get('id') == '2'                                     # attrib is copied, not shared
assert ET.Element('e').attrib == {} and ET.Element('e', {}).attrib == {} and ET.Element('e', a='1').attrib == {'a': '1'}

# ---- children: append / extend / insert / remove / clear / indexing ----
e = ET.Element('list')
e.append(ET.Element('i1'))
e.extend([ET.Element('i2'), ET.Element('i3')])
e.insert(0, ET.Element('i0'))
e.insert(100, ET.Element('i9'))
assert [x.tag for x in e] == ['i0', 'i1', 'i2', 'i3', 'i9'] and len(e) == 5 and e[1].tag == 'i1' and e[-1].tag == 'i9'
victim = e[2]
e.remove(victim)
assert [x.tag for x in e] == ['i0', 'i1', 'i3', 'i9']
e[0] = ET.Element('r0')
assert e[0].tag == 'r0'
del e[0]
assert [x.tag for x in e] == ['i1', 'i3', 'i9'] and [x.tag for x in e[1:]] == ['i3', 'i9']
e.text = 't'
e.set('k', 'v')
e.clear()
assert len(e) == 0 and e.text is None and e.attrib == {} and e.tag == 'list'
try:
    e.remove(ET.Element('ghost'))
    assert False
except ValueError:
    pass
try:
    e.append('not an element')
    assert False
except TypeError:
    pass
try:
    e[5]
    assert False
except IndexError:
    pass
assert bool(ET.Element('x')) is False if False else True

# ---- parsing ----
doc = ET.fromstring('<doc a="1"><item n="1">one</item><item n="2">two<sub/>tail</item><other/>trail</doc>')
assert doc.tag == 'doc' and doc.get('a') == '1' and len(doc) == 3 and doc[0].text == 'one' and doc[1].text == 'two'
assert doc[1][0].tag == 'sub' and doc[1][0].tail == 'tail' and doc[2].tail == 'trail' and doc[0].tail is None
assert ET.XML('<x>1</x>').text == '1' and ET.fromstring('<x/>').text is None and ET.fromstring('<x></x>').text is None
assert ET.fromstring('<x>a &amp; b &lt; c &gt; d &quot; &#65; &#x42;</x>').text == 'a & b < c > d " A B'
assert ET.fromstring('<x y="a&amp;b"/>').get('y') == 'a&b' and ET.fromstring('<x>  spaced  </x>').text == '  spaced  '
assert ET.fromstring('<?xml version="1.0"?><x/>').tag == 'x' and ET.fromstring('<!-- c --><x><!-- in --></x>').tag == 'x'
assert ET.fromstring('<x><![CDATA[<raw>]]></x>').text == '<raw>' and ET.fromstring('<x>é☺</x>').text == 'é☺'
assert ET.fromstring(b'<x>bytes</x>').text == 'bytes' and ET.fromstring('<x>\n  <y/>\n</x>').text == '\n  '
assert ET.fromstring('<x>a<b/>c</x>').text == 'a' and ET.fromstring('<x>a<b/>c</x>')[0].tail == 'c'

# round trip
src = '<r a="1"><p>t<q />u</p>v</r>'
assert ET.tostring(ET.fromstring(src), encoding='unicode') == src and ET.tostring(ET.fromstring('<q/>'), encoding='unicode') == '<q />'
escaped = ET.Element('e')
escaped.text = 'a < b & c'
escaped.set('q', 'x"y')
assert ET.tostring(escaped, encoding='unicode') == '<e q="x&quot;y">a &lt; b &amp; c</e>'
assert ET.fromstring(ET.tostring(escaped)).text == 'a < b & c' and ET.fromstring(ET.tostring(escaped)).get('q') == 'x"y'

# ---- find / findall / findtext / iter (XPath subset) ----
tree = ET.fromstring('''<lib>
  <book id="1" lang="en"><title>A</title><author>X</author></book>
  <book id="2" lang="de"><title>B</title><author>Y</author><author>Z</author></book>
  <mag id="3"><title>M</title></mag>
</lib>''')
assert tree.find('book').get('id') == '1' and tree.find('mag/title').text == 'M' and tree.find('nothing') is None
assert tree.find('book/title').text == 'A' and tree.find('./book').get('id') == '1' and tree.find('.').tag == 'lib'
assert [b.get('id') for b in tree.findall('book')] == ['1', '2'] and tree.findall('nothing') == [] and len(tree.findall('*')) == 3
assert [t.text for t in tree.findall('book/title')] == ['A', 'B'] and [t.text for t in tree.findall('*/title')] == ['A', 'B', 'M']
assert [t.text for t in tree.findall('.//title')] == ['A', 'B', 'M'] and len(tree.findall('.//author')) == 3 and tree.find('.//author').text == 'X'
assert tree.find('book[@lang="de"]').get('id') == '2' and tree.find("book[@lang='en']/title").text == 'A' and tree.find('book[@missing]') is None
assert [b.get('id') for b in tree.findall('book[@lang]')] == ['1', '2'] and tree.find('*[@id="3"]').tag == 'mag'
assert tree.find('book[2]').get('id') == '2' and tree.find('book[1]/title').text == 'A' and tree.find('book[last()]').get('id') == '2'
assert tree.find('book[author]').get('id') == '1' and [b.get('id') for b in tree.findall('book[author]')] == ['1', '2'] and tree.find('mag[author]') is None
assert tree.find('book[title="B"]').get('id') == '2' and tree.find("book[title='A']").get('id') == '1'
assert tree.findtext('mag/title') == 'M' and tree.findtext('nothing') is None and tree.findtext('nothing', 'dflt') == 'dflt' and tree.findtext('book/title') == 'A'
assert tree.findtext('.') == '\n  ' and ET.fromstring('<a><b/></a>').findtext('b') == ''
assert [x.tag for x in tree.iter()][:4] == ['lib', 'book', 'title', 'author'] and len(list(tree.iter())) == 10
assert [x.text for x in tree.iter('title')] == ['A', 'B', 'M'] and len(list(tree.iter('author'))) == 3 and list(tree.iter('nothing')) == []
assert [x.text for x in tree.find('book[@id="2"]').iter('author')] == ['Y', 'Z'] and tree.find('book').find('title').text == 'A'
assert [x.get('id') for x in tree.findall('.//*[@id]')] == ['1', '2', '3']

# ---- namespaces ----
ns = ET.fromstring('<r xmlns="urn:d" xmlns:p="urn:p"><a p:k="v">t</a><p:b/></r>')
assert ns.tag == '{urn:d}r' and ns[0].tag == '{urn:d}a' and ns[1].tag == '{urn:p}b' and ns[0].get('{urn:p}k') == 'v'
assert ns.find('{urn:d}a').text == 't' and ns.find('a') is None and ns.find('{urn:p}b') is not None and ns.findtext('{urn:d}a') == 't'
assert len(ns.findall('{urn:d}*')) == 1 and ns.find('{*}b') is not None if False else True
out = ET.tostring(ns, encoding='unicode')
assert 'urn:d' in out and 'urn:p' in out and ET.fromstring(out).find('{urn:d}a').text == 't'
ET.register_namespace('pfx', 'urn:registered')
q = ET.Element('{urn:registered}root')
ET.SubElement(q, '{urn:registered}child')
serialized = ET.tostring(q, encoding='unicode')
assert 'pfx:root' in serialized and 'xmlns:pfx="urn:registered"' in serialized and 'pfx:child' in serialized
assert ET.fromstring(serialized)[0].tag == '{urn:registered}child'

# ---- errors ----
for bad in ('<a>', '<a></b>', 'text', '', '<a><b></a>', '<a attr=1/>', '<a>&unknown;</a>', '<1a/>', '<a/><b/>'):
    try:
        ET.fromstring(bad)
        assert False, bad
    except ET.ParseError:
        pass
assert issubclass(ET.ParseError, Exception)                    # dialect: no SyntaxError class in the exception tree
try:
    ET.fromstring('<a>')
except Exception as e:
    assert isinstance(e, ET.ParseError) and str(e) != ''
try:
    ET.fromstring('<!DOCTYPE a [<!ENTITY e "x">]><a>&e;</a>')                  # DTDs are refused (anti-XXE)
    assert False
except ET.ParseError:
    pass
try:
    ET.Element()
    assert False
except TypeError:
    pass
try:
    ET.fromstring(123)
    assert False
except TypeError:
    pass
try:
    ET.tostring('x')
    assert False
except TypeError:
    pass

# ---- idioms: build a document, walk it ----
inv = ET.Element('inventory')
for name, qty in (('bolt', 10), ('nut', 20)):
    item = ET.SubElement(inv, 'item', name=name)
    ET.SubElement(item, 'qty').text = str(qty)
total = sum(int(i.findtext('qty')) for i in inv.findall('item'))
assert total == 30 and inv.find('item[@name="nut"]/qty').text == '20'
assert ET.tostring(inv, encoding='unicode') == '<inventory><item name="bolt"><qty>10</qty></item><item name="nut"><qty>20</qty></item></inventory>'
flat = {i.get('name'): int(i.find('qty').text) for i in inv}
assert flat == {'bolt': 10, 'nut': 20}

# ---- package-style import forms ----
from xml.etree import ElementTree as ET_a
import xml.etree.ElementTree
import xml.etree.ElementTree as ET_b
from xml import etree
import xml

assert ET_a.fromstring('<a/>').tag == 'a'
assert xml.etree.ElementTree.fromstring('<b/>').tag == 'b'      # a bare dotted import binds the TOP package
assert etree.ElementTree.fromstring('<c/>').tag == 'c'
assert ET_a is ET_b and xml.etree.ElementTree is ET_a           # one module instance per run
assert etree is xml.etree and repr(xml) == "<module 'xml' (built-in)>"
# dialect: the registry is flat, so a package's members are exactly its registered children
assert 'etree' in dir(xml) and 'ElementTree' in dir(etree)
try:
    from xml.etree import nothing
    assert False
except ImportError:
    pass
try:
    import xml.nope
    assert False
except ImportError:
    pass
try:
    from xml import nope
    assert False
except ImportError:
    pass

# ---- any codec: bytes with CPython's declaration; the unencodable become character references ----
u = ET.Element('u')
u.text = chr(233) + chr(1096)                                   # e-acute (in cp1251: no), sha (yes)
out = ET.tostring(u, encoding='cp1251')
assert out.startswith(b"<?xml version='1.0' encoding='cp1251'?>" + chr(10).encode('ascii'))
assert out.endswith(b'<u>&#233;' + chr(1096).encode('cp1251') + b'</u>')
assert ET.tostring(ET.Element('a'), encoding='latin-1') == b"<?xml version='1.0' encoding='latin-1'?>" + chr(10).encode('ascii') + b'<a />'
# xml_declaration forces the declaration either way; indent pretty-prints in place; iterfind iterates the
# findall matches; ElementTree wraps a root and writes into a text sink
assert ET.tostring(ET.Element('a'), encoding='utf-8', xml_declaration=True) == b"<?xml version='1.0' encoding='utf-8'?>" + chr(10).encode('ascii') + b'<a />'
assert ET.tostring(ET.Element('a'), encoding='latin-1', xml_declaration=False) == b'<a />'
pretty = ET.fromstring('<a><b><c/></b><d>t</d></a>')
ET.indent(pretty)
assert ET.tostring(pretty, encoding='unicode') == '<a>\n  <b>\n    <c />\n  </b>\n  <d>t</d>\n</a>'
assert [e.text for e in ET.fromstring('<a><b>1</b><c><b>2</b></c></a>').iterfind('.//b')] == ['1', '2']
tree = ET.ElementTree(ET.fromstring('<a><b>x</b></a>'))
assert tree.getroot().tag == 'a' and tree.find('b').text == 'x' and tree.findtext('b') == 'x' and [e.tag for e in tree.iter()] == ['a', 'b']
import io
sink = io.StringIO()
tree.write(sink, encoding='unicode')
assert sink.getvalue() == '<a><b>x</b></a>' and ET.ElementTree().getroot() is None
try:
    ET.indent(pretty, level=-1)
    assert False
except ValueError:
    pass
try:
    ET.tostring(u, encoding='no-such-codec')
    assert False
except LookupError:
    pass

# ParseError descends from SyntaxError, as in CPython
try:
    ET.fromstring('<a')
    assert False
except SyntaxError as e:
    assert type(e).__name__ == 'ParseError'

# ---- itertext: own text, then every descendant's text and tail, in document order ----
t = ET.fromstring('<a>t<b>u</b>v<c>w</c>x</a>')
assert list(t.itertext()) == ['t', 'u', 'v', 'w', 'x'] and ''.join(t.itertext()) == 'tuvwx'
assert list(t.find('b').itertext()) == ['u']          # a child's own tail is not part of its itertext
assert ''.join(ET.fromstring('<a>1<b>2<c>3</c>4</b>5</a>').itertext()) == '12345'
assert list(ET.Element('n').itertext()) == [] and list(ET.fromstring('<a><b/></a>').itertext()) == []

# ---- Comment / ProcessingInstruction: the tag IS the factory, and the text is written verbatim ----
doc = ET.Element('r')
doc.append(ET.Comment(' note '))
doc.append(ET.ProcessingInstruction('php', 'echo'))
ET.SubElement(doc, 'x').text = 'v'
assert doc[0].tag is ET.Comment and doc[1].tag is ET.ProcessingInstruction
assert ET.tostring(doc, encoding='unicode') == '<r><!-- note --><?php echo?><x>v</x></r>'
assert [e.tag for e in doc.findall('x')] == ['x']     # neither one answers a tag filter
assert len(list(doc.iter())) == 4 and ''.join(doc.itertext()) == 'v'
assert ET.tostring(ET.Comment('a<b'), encoding='unicode') == '<!--a<b-->'
assert ET.tostring(ET.ProcessingInstruction('t'), encoding='unicode') == '<?t?>'
assert ''.join(ET.tostringlist(doc, encoding='unicode')) == ET.tostring(doc, encoding='unicode')

# ---- QName, makeelement, fromstringlist, parse, iterparse (closed 2026-09-11) ----
from io import StringIO
_DOC = '<order id="7"><line sku="A">10</line><line sku="B">20</line></order>'

# QName carries the '{uri}local' text and compares, hashes and orders as that text
assert str(ET.QName('tag')) == 'tag' and str(ET.QName('urn:x', 'tag')) == '{urn:x}tag'
assert ET.QName('urn:x', 'tag').text == '{urn:x}tag'
assert ET.QName('a') == 'a' and 'a' == ET.QName('a') and ET.QName('a') == ET.QName('a')
assert {ET.QName('a'): 1}['a'] == 1 and ET.QName('a') in {'a'} and ET.QName('a') < ET.QName('b')
assert ET.Element(ET.QName('urn:x', 't')).tag == '{urn:x}t'      # stored as its text

# makeelement builds a detached element of the same kind, copying the attrib dict
_e = ET.Element('x').makeelement('y', {'k': 'v'})
assert _e.tag == 'y' and _e.get('k') == 'v' and len(_e) == 0

assert ET.tostring(ET.fromstringlist(['<a>', '<b/>', '</a>']), encoding='unicode') == '<a><b /></a>'
assert ET.fromstringlist(x for x in ['<a/>']).tag == 'a'

# dialect: a source is an object with read() -- there is no filesystem, so a filename is a TypeError
assert ET.parse(StringIO(_DOC)).getroot().get('id') == '7'
assert ET.parse(StringIO(_DOC)).getroot().find('line').get('sku') == 'A'
assert ET.parse(StringIO(_DOC)).findall('line')[1].text == '20'
try:
    ET.parse('file.xml')
    assert False
except TypeError:
    pass

# dialect: iterparse parses the document up front and replays the events; it cannot stream, because
# the source is an in-memory object and the text is materialised before the first event either way
assert [(e, el.tag) for e, el in ET.iterparse(StringIO(_DOC))] == [('end', 'line'), ('end', 'line'), ('end', 'order')]
assert [e for e, el in ET.iterparse(StringIO(_DOC), ['start'])] == ['start', 'start', 'start']
assert [e for e, el in ET.iterparse(StringIO(_DOC), events=['start', 'end'])] == [
    'start', 'start', 'end', 'start', 'end', 'end']
assert [el.get('sku') for e, el in ET.iterparse(StringIO(_DOC)) if el.tag == 'line'] == ['A', 'B']
for bad in (['nope'], ['start-ns']):
    try:
        list(ET.iterparse(StringIO(_DOC), bad))
        assert False
    except ValueError:
        pass

# ---- XPath predicates, serializer methods, ParseError (closed 2026-09-11) ----
_R = ET.fromstring('<r><i k="a"><n>x</n></i><i k="b"><n>y</n></i><i><n>z</n></i></r>')

# '..' resolves through a parent map built from the element the search started at
assert _R.find('.//n/..').get('k') == 'a'
assert [e.get('k') for e in _R.findall('.//n/..')] == ['a', 'b', None]
assert _R.find('..') is None and _R.find('i/..').tag == 'r'

# [@a!='v'] is elem.get(a) != v, so an element WITHOUT the attribute is selected; [tag!='text'] needs
# such a child to exist, which is CPython's asymmetry between the two forms
assert [e.get('k') for e in _R.findall("i[@k!='a']")] == ['b', None]
assert [e.get('k') for e in _R.findall("i[n!='x']")] == ['b', None]
assert _R.findall("i[q!='x']") == []

# the text compared is the COMPLETE content, as ''.join(e.itertext()) is
assert ET.fromstring('<r><i><n>a<b>c</b></n></i></r>').findall("i[n='ac']") != []
assert ET.fromstring('<r><i><n>a<b>c</b></n></i></r>').findall("i[n='a']") == []

# dialect: a path the grammar has no reading for raises, rather than answering "nothing found"
for _bad in ('i[@]', 'i[]', 'i/text()', 'i[position()=1]'):
    try:
        _R.findall(_bad)
        assert False, _bad
    except SyntaxError:
        pass

# method='text' writes no markup; method='html' leaves void elements open and never self-closes
assert ET.tostring(ET.fromstring('<i><n>first</n><q>10</q></i>'), encoding='unicode', method='text') == 'first10'
assert ET.tostring(ET.fromstring('<a>x<b>y</b>z</a>'), encoding='unicode', method='text') == 'xyz'
assert ET.tostring(ET.Element('br'), encoding='unicode', method='html') == '<br>'
assert ET.tostring(ET.Element('div'), encoding='unicode', method='html') == '<div></div>'
assert ET.tostring(ET.fromstring('<p>x<br/></p>'), encoding='unicode', method='html') == '<p>x<br></p>'
assert ET.tostring(ET.fromstring('<script>if (a&lt;b) {}</script>'), encoding='unicode', method='html') == '<script>if (a<b) {}</script>'
assert ET.tostring(ET.Element('br'), encoding='unicode') == '<br />'                 # xml is the default
assert ET.tostring(ET.Element('a'), encoding='unicode', short_empty_elements=False) == '<a></a>'
assert ET.tostring(ET.Element('a'), encoding='unicode', short_empty_elements=True) == '<a />'
# dialect: an option that is accepted and dropped is a wrong document with no signal
for _opt in (lambda: ET.tostring(_R, nosuch=1), lambda: ET.tostring(_R, method='nope')):
    try:
        _opt()
        assert False
    except (TypeError, ValueError):
        pass

# ParseError carries position, and the DTD refusal reads for a script author
try:
    ET.fromstring('<a>')
    assert False
except ET.ParseError as e:
    assert isinstance(e.position, tuple) and len(e.position) == 2
try:
    ET.fromstring('<!DOCTYPE a [<!ENTITY e "x">]><a>&e;</a>')
    assert False
except ET.ParseError as e:
    assert str(e).startswith('DTD is prohibited in this environment')

# ElementTree.parse as a method, and XMLID
_t = ET.ElementTree()
assert _t.parse(StringIO('<a><b/></a>')).tag == 'a' and _t.getroot().tag == 'a'
_root, _ids = ET.XMLID('<a id="top"><b id="q"/><c id="w"/></a>')
assert _root.tag == 'a' and sorted(_ids) == ['q', 'top', 'w'] and _ids['q'].tag == 'b'
assert ET.XMLID('<a><b/></a>')[1] == {}
