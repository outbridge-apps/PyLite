# Review 13, row 24: lent walks are never shared (iterators interleaved, asked again, a search in a tag's __eq__).
import xml.etree.ElementTree as ET
root = ET.fromstring('<r><a>1<b>2<c>3</c>t</b>u</a><a>4</a></r>')
it = root.iter(); print([e.tag for e in it], [e.tag for e in it])
tx = root.itertext(); print(list(tx), list(tx))
i1 = root.iter('a'); i2 = root.iter(); i3 = root.itertext()
out = []
for k in range(8):
    out.append((next(i1, None) is not None, getattr(next(i2, None), "tag", "-"), next(i3, '-')))
print(out)
aux = ET.fromstring('<q><a>1<b>2<c>3</c>t</b>u</a><a>4</a></q>')
class Odd:
    def __eq__(self, o):
        return len(aux.findall('.//c')) == 1 and ''.join(aux.itertext()) == '123tu4' and o == 'zz'
    def __hash__(self):
        return 1
x = ET.SubElement(root, Odd())
print(len(list(root.iter('zz'))), [e.tag for e in root.iter('a')])
print(len(root.findall(".//b[.='23t']")), len(root.findall("a[.='123tu']")), root.findtext('a'), ET.tostring(root[0], method='text', encoding='unicode'))
d = ET.Element('d'); n = d
for i in range(50):
    n = ET.SubElement(n, 'x'); n.text = str(i % 10)
it = d.iter(); first = [next(it).tag for _ in range(5)]
print(first, len(d.findall(".//x[.='9']")), len(list(d.iter())), sum(1 for _ in it))
t1 = d.itertext(); a = next(t1); print(a, len(''.join(d.itertext())), len(list(t1)))
def bad():
    e = ET.Element('e'); s = ET.SubElement(e, 's'); s.text = 5
    try:
        e.findall("s[.='5']")
    except TypeError as ex:
        print('TE', ex)
for _ in range(3):
    bad()
print(len(d.findall(".//x[.='9']")))
