import xml.etree.ElementTree as ET
import copy

def show(label, f):
    try:
        print(label, repr(f()))
    except Exception as e:
        print(label, 'EXC', type(e).__name__, str(e)[:90])

e = ET.Element('a')
show('1', lambda: (e.attrib, e.keys(), e.items(), e.get('x'), e.get('x', 5), len(e.attrib)))
a1 = e.attrib
show('2', lambda: e.attrib is a1)
e.set('k', 'v')
show('3', lambda: (a1, e.attrib is a1))
e.clear()
show('4', lambda: (a1, e.attrib is a1, e.attrib))
d = {}
e.attrib = d
show('5', lambda: e.attrib is d)
c = copy.copy(e)
d['z'] = '1'
show('6', lambda: (c.attrib, c.get('z'), e.get('z')))
e2 = ET.Element('b', {'p': '1'})
c2 = copy.copy(e2)
c2.set('q', '2')
show('7', lambda: (e2.attrib, c2.attrib, e2.attrib is c2.attrib))
dc = copy.deepcopy(e2)
show('8', lambda: (dc.attrib, dc.attrib is e2.attrib))
e3 = ET.Element('c')
_ = e3.attrib
lst = copy.deepcopy([e3, e3.attrib])
show('9', lambda: (lst[0].attrib is lst[1], lst[1]))
e4 = ET.Element('d', {'x': '1'})
lst = copy.deepcopy([e4, e4.attrib])
show('10', lambda: (lst[0].attrib is lst[1], lst[1]))
show('11', lambda: ET.tostring(ET.Element('e')))
show('12', lambda: ET.tostring(ET.Element('e', {})))
show('13', lambda: ET.tostring(ET.Element('{u}e', {'{v}a': '1'})))
m = e2.makeelement('m', {})
show('14', lambda: (m.attrib, m.tag))
d2 = {'r': '1'}
m2 = e2.makeelement('m', d2)
show('15', lambda: (m2.attrib is d2, m2.attrib))
root = ET.fromstring('<r><i id="1"/><i/><i id="2" k="v"/></r>')
show('16', lambda: [x.attrib for x in root])
show('17', lambda: [x.get('id') for x in root.findall("i[@id]")])
show('18', lambda: [x.get('id') for x in root.findall("i[@id='2']")])
show('19', lambda: [x.get('id') for x in root.findall("i[@id!='2']")])
show('20', lambda: ET.tostring(root))
doc, ids = ET.XMLID('<r><i id="1"/><i/><j id="2"/></r>')
show('21', lambda: sorted(ids))
show('22', lambda: ET.SubElement(root, 's', {}, q='1').attrib)
show('23', lambda: ET.SubElement(root, 's', {}).attrib)
show('24', lambda: ET.Element('x', attrib={}).attrib)
show('25', lambda: ET.Element('x', attrib={'a': 'b'}, c='d').attrib)
pi = ET.PI('t', 'x')
show('26', lambda: (pi.attrib, ET.tostring(pi)))
cm = ET.Comment('c')
show('27', lambda: (cm.attrib, cm.keys(), ET.tostring(cm)))
show('28', lambda: copy.copy(cm).attrib)
show('29', lambda: ET.tostring(copy.deepcopy(root)))
e5 = ET.Element('f')
e5.attrib['k'] = 'v'
show('30', lambda: ET.tostring(e5))
e6 = ET.Element('g')
x = e6.attrib
x['late'] = '1'
show('31', lambda: (e6.get('late'), ET.tostring(e6), e6.keys()))
e6.clear()
x['later'] = '2'
show('32', lambda: (e6.get('later'), ET.tostring(e6)))
show('33', lambda: ET.tostring(ET.Element('h', attrib=None)) )
class K:
    def __hash__(self): return hash('id')
    def __eq__(self, o): return o == 'id'
e7 = ET.Element('k')
e7.attrib[K()] = 'v'
show('34', lambda: e7.get('id'))
show('35', lambda: [x.tag for x in ET.Element('r2').findall('k')])
r8 = ET.Element('r8'); r8.append(e7)
show('36', lambda: [x.tag for x in r8.findall("k[@id]")])
show('37', lambda: [x.tag for x in r8.findall("k[@id='v']")])
show('38', lambda: ET.tostring(copy.copy(ET.Element('n', {'a': '1'}))))
show('39', lambda: ET.fromstring('<a/>').attrib)
show('40', lambda: list(ET.iterparse(__import__('io').BytesIO(b'<a x="1"><b/></a>'), ('start','end'))))
