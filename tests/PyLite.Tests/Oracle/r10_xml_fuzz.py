# Review 10: a fuzz of ElementPath positions, lazy attrib and iterparse. Same output expected from CPython.
import xml.etree.ElementTree as ET
import copy, io

state = [12345]
def rnd(n):
    state[0] = (state[0] * 1103515245 + 12345) % 2147483648
    return (state[0] >> 8) % n

TAGS = ['a', 'b', 'c']
PATHS = ['a[1]', 'a[last()]', 'b[2]', 'a[last()-1]', '*[1]', '*[last()]', 'a/b[1]', 'a/b[last()]', '*/a[2]',
         './/a[1]', './/b[last()]', 'a[1]/..', 'b/..', './/c/..', 'a[@k]', 'a[@k][1]', 'a[1][@k]', 'b[c]',
         'a[1]/b', '*/*[last()]', '.', 'a[3]', 'c[last()-2]', '{}a[1]', 'a[b][last()]', '..', './/a/../b[1]']

def ident(e):
    return e.get('id', '?')

def build(n, share):
    root = ET.Element('r', id='r')
    nodes = [root]
    for i in range(n):
        p = nodes[rnd(len(nodes))]
        kw = {'id': str(i)}
        if rnd(4) == 0:
            kw['k'] = 'v'
        e = ET.SubElement(p, TAGS[rnd(3)], kw)
        nodes.append(e)
    for j in range(share):
        x = nodes[1 + rnd(len(nodes) - 1)]
        q = nodes[rnd(len(nodes))]
        op = rnd(5)
        if op == 0:
            q.append(x)             # x in two lists (or a cycle: skipped below)
        elif op == 1:
            for par in nodes:
                if x in list(par):
                    par.remove(x)
                    break
            q.insert(rnd(len(q) + 1), x)   # a move
        elif op == 2:
            q.extend([ET.Element(TAGS[rnd(3)], id='n%d' % j)])
        elif op == 3 and len(q):
            q[rnd(len(q))] = ET.Element(TAGS[rnd(3)], id='s%d' % j)
        elif op == 4 and len(q):
            del q[rnd(len(q))]
    return root, nodes

def acyclic(root):
    seen = set()
    stack = [(root, 0)]
    count = 0
    while stack:
        e, d = stack.pop()
        count += 1
        if d > 40 or count > 5000:
            return False
        for c in e:
            stack.append((c, d + 1))
    return True

out = []
cases = 0
for t in range(80):
    n = 3 + rnd(25)
    share = rnd(3) if t % 3 else 0
    root, nodes = build(n, share)
    if not acyclic(root):
        continue
    if t % 7 == 0:
        root = copy.deepcopy(root)
    elif t % 7 == 1:
        root2 = copy.copy(root)
        root = ET.Element('w', id='w')
        root.append(root2)
        root.append(copy.copy(root2))
    starts = [root] + [nodes[rnd(len(nodes))] for _ in range(2)]
    for st in starts:
        if not acyclic(st):
            continue
        for p in PATHS:
            try:
                r = [ident(e) for e in st.findall(p)]
                f = st.find(p)
                f = None if f is None else ident(f)
                it = [ident(e) for e in st.iterfind(p)]
                print('%d %s %s %s %s %s' % (t, ident(st), p, r, f, it == r))
            except Exception as ex:
                print('%d %s %s %s' % (t, ident(st), p, type(ex).__name__))
            cases += 1
    # mutate between finds: the cached maps must follow
    for k in range(6):
        ET.SubElement(root, TAGS[rnd(3)], id='m%d_%d' % (t, k))
        print('%d m %s %s' % (t, [ident(e) for e in root.findall('a[last()]')],
                                   [ident(e) for e in root.findall('*[1]')]))

# lazy attrib: CPython keeps none until asked
e = ET.Element('x')
c = copy.copy(e)
e.set('a', '1')
print('copy-empty %s %s' % (c.get('a'), e.get('a')))
e2 = ET.Element('x', {'a': '1'})
c2 = copy.copy(e2)
e2.set('b', '2')
print('copy-shared %s' % sorted(c2.items()))
e3 = ET.Element('x')
d = e3.attrib
d['z'] = '9'
print('attrib-live %s %s %s' % (e3.get('z'), e3.attrib is d, e3.keys()))
e3.clear()
print('after-clear %s %s %s' % (e3.attrib is d, e3.attrib, d))
e4 = ET.Element('x', {})
print('empty-ctor %s %s %s' % (e4.items(), e4.keys(), e4.get('q', 'dflt')))
e5 = e4.makeelement('y', {})
e5.set('p', '1')
print('makeelement %s %s' % (e5.items(), e4.items()))
dc = copy.deepcopy(ET.fromstring('<a x="1"><b/><c y="2"/></a>'))
print('deepcopy %s' % ET.tostring(dc))
r = ET.fromstring('<r xmlns:q="u"><s q:t="1"/><s/></r>')
print('parsed %s %s %s' % ([x.attrib for x in r], r.attrib, ET.tostring(r)))
print('xpath-attr %s' % [x.tag for x in r.findall('*[@{u}t]')])
el = ET.Element('t')
el.attrib = {'n': 'm'}
print('set-attrib %s %s' % (el.get('n'), ET.tostring(el)))
x1, ids = ET.XMLID('<r><a id="1"/><b/></r>')
print('xmlid %s' % sorted(ids))

# iterparse: events are the parse's, whatever the script does to the tree
doc = '<root>' + ''.join('<rec id="%d"><v>%d</v><w/></rec>' % (i, i) for i in range(12)) + '</root>'
seen = []
ctx = ET.iterparse(io.StringIO(doc), events=('start', 'end'))
ev0, root = next(ctx)
for ev, el in ctx:
    if ev == 'end' and el.tag == 'rec':
        seen.append(el.get('id') + ':' + el.findtext('v', ''))
        root.clear()
print('iterparse-clear %s %d' % (seen, len(root)))
seen = []
for ev, el in ET.iterparse(io.StringIO(doc), events=('start', 'end')):
    seen.append(ev[0] + el.tag)
    if ev == 'start' and el.tag == 'rec':
        el.append(ET.Element('added'))      # no events for it
        if el.get('id') == '3':
            el.clear()
print('iterparse-mutate %s' % ''.join(seen))
seen = []
for ev, el in ET.iterparse(io.StringIO('<a><b/><c><d/></c></a>'), events=('start',)):
    seen.append('%s%d' % (el.tag, len(el)))
print('iterparse-start %s' % seen)
it = ET.iterparse(io.StringIO('<a><b/></a>'))
print('iterparse-default %s' % [(e, x.tag) for e, x in it])

out = []

print('cases', cases, 'lines', len(out))
