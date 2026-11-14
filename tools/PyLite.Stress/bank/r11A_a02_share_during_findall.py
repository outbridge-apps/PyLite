import xml.etree.ElementTree as ET

# eager findall: an attribute key's __eq__ (run by the [@k] lookup) shares the candidate into a second parent
r = ET.fromstring('<r><a><x k="1"/></a><a><x/></a></r>')
b = ET.Element('b')

class K:
    def __init__(self, s):
        self.s = s
    def __hash__(self):
        return hash(self.s)
    def __eq__(self, other):
        b.append(r[1][0])
        return other == self.s

r[1][0].attrib[K('k')] = '2'
print(len(r.findall('a/x[@k][1]')))
# the same through a removal-free 'detach then insert elsewhere'
r = ET.fromstring('<r><a><x k="1"/></a><a><x k="1"/></a></r>')
out = ET.Element('out')
got = []
for x in r.iterfind('a/x[1]'):
    got.append(x.get('k'))
    ET.SubElement(out, 'log')
print(got)
