# repeat: 2
# deadline: 20000
import xml.etree.ElementTree as ET
import io, re, copy, itertools, collections, json
B = chr(92)
doc = '<r>' + ''.join('<row id="%d" k="v%d"><c a="1">x%d</c><d/></row>' % (i, i, i) for i in range(3000)) + '</r>'
root = ET.fromstring(doc)
n = 0
for ev, el in ET.iterparse(io.StringIO(doc), events=('start', 'end')):
    if ev == 'end' and el.tag == 'row':
        n += len(el.attrib)
        root2 = el
its = [root.iterfind('.//row[2]') for i in range(30)]
for it in its:
    next(it)
for i in range(200):
    r = root.find('row[last()]')
    root.append(copy.copy(r))
for e in root.iter('c'):
    e.attrib
    e.set('z', '1')
s = ET.tostring(root)
dc = copy.deepcopy(root)
p = [re.compile(B + 'U%08x+' % (0x1F600 + i)) for i in range(300)]
m = sum(1 for x in p if x.search(chr(0x1F600) * 3))
cnt = len(re.findall(r'\w+', 'abc def ' * 20000))
w = json.dumps({'a': [list(range(100))] * 100})
print(n, len(s), m, cnt, len(w), len(dc))
