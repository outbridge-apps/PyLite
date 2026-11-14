# deadline: 20000
import xml.etree.ElementTree as ET
import copy

def show(label, f):
    try:
        print(label, repr(f()))
    except Exception as e:
        print(label, 'EXC', type(e).__name__, str(e)[:100])

def tags(lst):
    return [x.tag if isinstance(x.tag, str) else 'T' for x in lst]

# 1. a non-str tag whose __eq__ moves rows around, mid findall with positions
class MoveTag:
    def __init__(self, root): self.root = root; self.n = 0
    def __eq__(self, o):
        self.n += 1
        if self.n < 4 and len(self.root):
            x = self.root[0]
            self.root.remove(x)
            self.root.append(x)
        return isinstance(o, MoveTag)
    def __hash__(self): return 1
def mk():
    r = ET.Element('r')
    for i in range(5):
        e = ET.SubElement(r, 'row'); e.text = str(i)
    return r
r = mk(); t = ET.Element('z'); t.tag = MoveTag(r); r.insert(2, t)
show('1a', lambda: [x.text for x in r.findall('row[1]')])
r = mk(); t = ET.Element('z'); t.tag = MoveTag(r); r.insert(2, t)
show('1b', lambda: [x.text for x in r.findall('row[last()]')])
r = mk(); t = ET.Element('z'); t.tag = MoveTag(r); r.insert(2, t)
show('1c', lambda: [x.text if isinstance(x.tag, str) else 'T' for x in r.findall('*[2]')])
r = mk(); t = ET.Element('z'); t.tag = MoveTag(r); r.insert(2, t)
show('1d', lambda: [x.text if isinstance(x.tag, str) else 'T' for x in r.findall('*[last()]')])
r = mk(); t = ET.Element('z'); t.tag = MoveTag(r); r.insert(4, t)
show('1e', lambda: [x.text for x in r.findall('.//row[2]')])

# 2. text predicate whose __bool__ mutates the tree, then positions
class B:
    def __init__(self, f): self.f = f
    def __bool__(self):
        self.f(); return False
r = mk()
def mut1():
    if len(r) > 2:
        x = r[-1]; r.remove(x); r.insert(0, x)
r[1].text = B(mut1)
show('2a', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('row[.="q"][1]')])
r = mk(); r[1].text = B(mut1)
show('2b', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('row[1][.="0"]')])
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
r[1][0].text = B(mut1)
show('2c', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('row[c="k"][last()]')])
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
r[0][0].text = B(mut1)
show('2d', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('row[c="z"]/../row[2]')])
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
def mut2():
    # make an element shared: append row 0 also under row 3
    if len(r[3]) < 3:
        r[3].append(r[0])
r[1][0].text = B(mut2)
show('2e', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('.//row[1]')])
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
r[1][0].text = B(mut2)
show('2f', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('*[c="q"][1]')])
show('2g', lambda: [x.text if isinstance(x.text, str) else 'B' for x in r.findall('.//c[1]/..')])

# 3. re-entrant iterfind from __bool__
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
it = None
got = []
class Re:
    def __bool__(self):
        try:
            got.append(next(it).text)
        except StopIteration:
            got.append('stop')
        except Exception as e:
            got.append(type(e).__name__)
        return False
r[1][0].text = Re()
it = r.iterfind('row[c="k"][1]')
show('3a', lambda: ([x.text for x in it], got))
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
got = []
class Drain:
    def __bool__(self):
        try:
            for y in it:
                got.append(y.text if isinstance(y.text, str) else '?')
        except Exception as e:
            got.append(type(e).__name__)
        return False
r[0][0].text = Drain()
it = r.iterfind('*/c[.="k"]/..')
show('3b', lambda: ([x.text for x in it], got))
r = mk()
for x in r:
    ET.SubElement(x, 'c').text = 'k'
got = []
r[0][0].text = Drain()
it = r.iterfind('row[c="k"]/c/..[1]')
show('3c', lambda: ([x.text for x in it], got))
r = mk()
got = []
r[0].text = Drain()
it = r.iterfind('row[.="k"][last()]')
show('3d', lambda: ([x.text for x in it], got))

# 4. lazy iterfind with mutation between pulls (positions)
r = mk()
out = []
for x in r.iterfind('row[last()]'):
    out.append(x.text)
    ET.SubElement(r, 'row').text = 'n'
    if len(out) > 5: break
show('4a', lambda: out)
r = mk()
out = []
for x in r.iterfind('row[1]'):
    out.append(x.text)
    r.remove(x)
show('4b', lambda: (out, [y.text for y in r]))
r = mk()
out = []
for x in r.iterfind('row[2]'):
    out.append(x.text)
    r.insert(0, ET.Element('row'))
show('4c', lambda: (out, len(r)))
# many iterfinds kept
r = mk()
its = [r.iterfind('row[%d]' % (i % 5 + 1)) for i in range(2000)]
show('4d', lambda: [next(i).text for i in its[:7]])
for k in range(3):
    ET.SubElement(r, 'row').text = 'm'
show('4e', lambda: [next(i, None) for i in its[7:9]])
show('4f', lambda: [x.text for x in r.findall('row[last()]')])

# 5. shared element / copy then fast path
r = mk()
c = copy.copy(r)
show('5a', lambda: [x.text for x in c.findall('row[2]')])
c.append(ET.Element('row'))
show('5b', lambda: ([x.text for x in r.findall('row[last()]')], [x.text for x in c.findall('row[last()]')]))
r = mk(); r.append(r[0])
show('5c', lambda: [x.text for x in r.findall('row[1]')])
show('5d', lambda: [x.text for x in r.findall('row[last()]')])
r = mk(); r[0:0] = [r[4]]
show('5e', lambda: [x.text for x in r.findall('row[1]')])
r = mk(); r[1] = r[3]
show('5f', lambda: [x.text for x in r.findall('row[2]')], )
show('5g', lambda: [x.text for x in r.findall('row[4]')])
# cycle without sharing: root inside its child
r = ET.Element('r'); y = ET.SubElement(r, 'y'); y.append(r)
show('6a', lambda: tags(r.findall('y/r[1]')))
show('6b', lambda: tags(r.findall('y[1]')))
show('6c', lambda: tags(r.findall('y/r/y[1]')))
show('6d', lambda: tags(r.findall('y/r/y/..')))
