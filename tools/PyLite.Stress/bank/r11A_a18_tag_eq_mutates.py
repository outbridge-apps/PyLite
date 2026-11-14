import xml.etree.ElementTree as ET
r = ET.Element('r')
kids = [ET.SubElement(r, 'x') for _ in range(4)]
other = ET.Element('o')
calls = [0]

class T:
    def __init__(self, s):
        self.s = s
    def __eq__(self, o):
        calls[0] += 1
        if calls[0] < 50:
            other.append(kids[calls[0] % 4])      # share a sibling mid-walk
            r.insert(0, ET.Element('x'))          # and insert one before it
            if len(r) > 6:
                del r[-1]
        return isinstance(o, T)
    def __hash__(self):
        return 1

for k in kids:
    k.tag = T('x')
for path in ('*[1]', '*[2]', '*[last()]', './/*[1]', '*[1][1]', '..', '*/..'):
    try:
        res = r.findall(path)
        print(path, len(res), calls[0])
    except Exception as e:
        print(path, type(e).__name__, e)
for path in ('*[1]', './/*[last()]'):
    got = []
    try:
        for e in r.iterfind(path):
            got.append(e)
            r.append(ET.Element('y'))
            if len(got) > 20:
                break
        print('lazy', path, len(got))
    except Exception as e:
        print('lazy', path, type(e).__name__, e)
