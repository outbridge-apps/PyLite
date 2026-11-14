import xml.etree.ElementTree as ET
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
print(r[1][0].get('k'), len(b))
print(len(r.findall('a/x[@k]')), len(b))
