import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:400])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:200])
def mk(n=4, tag='row'):
    doc = ET.Element('doc', n='doc')
    for i in range(n):
        r = ET.SubElement(doc, tag, n=str(i))
        ET.SubElement(r, 'x', k='x%d' % i)
        ET.SubElement(r, 'x', k='y%d' % i)
    return doc
def ns(items):
    return [e.get('n') or e.get('k') for e in items]

def b1():
    doc = mk(3)
    it = doc.iterfind('row/..')
    got = [next(it).get('n')]
    ET.SubElement(doc, 'row', n='late')
    got += ns(it)
    it = doc.iterfind('row/x/..')
    got.append(next(it).get('n'))
    r = ET.SubElement(doc, 'row', n='late2')
    ET.SubElement(r, 'x', k='lx')
    ET.SubElement(doc[0], 'x', k='lx0')
    got += ns(it)
    got += ns(doc.iterfind('row/x/..'))
    return got
t('parent_walk_then_leaves', b1)
def b2():
    doc = mk(3)
    it = doc.iterfind('.//x/..')
    got = [next(it).get('n')]
    r = ET.SubElement(doc, 'row', n='late')
    ET.SubElement(r, 'x', k='lx')
    ET.SubElement(doc[1], 'x', k='lx1')
    got += ns(it)
    got += ns(doc.findall('.//x/..'))
    got += ns(doc.findall('.//x[3]'))
    got += ns(doc.findall('.//x[last()]'))
    return got
t('desc_parent_walk_then_leaves', b2)
def b3():
    doc = mk(4)
    other = ET.Element('other', n='other')
    it = doc.iterfind('row[2]/x')
    got = [next(it).get('k')]
    for i in range(5):
        ET.SubElement(ET.SubElement(other, 'row', n='o%d' % i), 'x', k='ox%d' % i)
        got += ns(doc.findall('row/x/..')[-1:])
        got += ns(other.findall('row/x/..')[-1:])
        got += ns(doc.findall('row[last()]'))
        got += ns(other.findall('row[last()]/x'))
    got += ns(it)
    return got
t('two_trees', b3)
def b4():
    doc = mk(2)
    got = ns(doc.findall('.//x/..'))
    leaf = ET.Element('row', n='leaf')
    doc.append(leaf)
    got += ns(doc.findall('.//row/..')) + ns(doc.findall('row[last()]')) + ns(doc.findall('.//x/..'))
    ET.SubElement(leaf, 'x', k='lx')
    got += ns(doc.findall('.//x/..')) + ns(doc.findall('.//x[1]')) + ns(doc.findall('row[last()]/x/..'))
    leaf.extend([ET.Element('x', k='e1'), ET.Element('x', k='e2')])
    got += ns(doc.findall('.//x[last()]')) + ns(doc.findall('.//x[2]/..'))
    leaf.insert(0, ET.Element('x', k='i0'))
    got += ns(doc.findall('.//x[1]')) + ns(doc.findall('.//x[4]/..'))
    return got
t('leaf_grows', b4)
def b5():
    doc = mk(3)
    leaf = ET.Element('x', k='shared')
    got = ns(doc.findall('.//x/..'))
    doc[0].append(leaf)
    got += ns(doc.findall('.//x[3]/..'))
    doc[2].append(leaf)
    got += ns(doc.findall('.//x[3]/..')) + ns(doc.findall('.//x[3]')) + ns(doc.findall('row/x[last()]'))
    doc[0].remove(leaf)
    got += ns(doc.findall('.//x[3]/..')) + ns(doc.findall('row/x[last()]'))
    return got
t('leaf_in_two_lists', b5)
def b6():
    doc = mk(3)
    other = ET.Element('other', n='other')
    leaf = ET.Element('x', k='L')
    got = ns(doc.findall('.//x/..')) + ns(other.findall('.//x/..'))
    doc[1].append(leaf)
    got += ns(doc.findall('.//x[3]/..')) + ns(other.findall('.//x/..'))
    other.append(leaf)
    got += ns(doc.findall('.//x[3]/..')) + ns(other.findall('.//x/..')) + ns(other.findall('x[1]'))
    doc[1].remove(leaf)
    got += ns(doc.findall('.//x[3]/..')) + ns(other.findall('x/..'))
    return got
t('leaf_then_other_tree', b6)
def b7():
    doc = mk(3)
    sub = doc[0]
    got = ns(sub.findall('x/..')) + ns(doc.findall('row/x/..'))
    ET.SubElement(doc[1], 'x', k='n1')
    got += ns(sub.findall('x[last()]')) + ns(doc.findall('row/x[last()]'))
    ET.SubElement(sub, 'x', k='n0')
    got += ns(sub.findall('x[last()]')) + ns(doc.findall('row/x[last()]')) + ns(sub.findall('x[3]/..'))
    return got
t('subtree_and_whole', b7)
def b8():
    doc = mk(2)
    leaf = ET.Element('row', n='L')
    got = ns(doc.findall('row/..'))
    doc.append(leaf)
    got += ns(doc.findall('row[3]'))
    doc.remove(leaf)
    got += ns(doc.findall('row[3]')) + ns(doc.findall('row[last()]'))
    doc.append(leaf)
    got += ns(doc.findall('row[3]')) + ns(doc.findall('row[last()]/..'))
    doc.insert(0, leaf)
    got += ns(doc.findall('row[1]')) + ns(doc.findall('row[last()]')) + ns(doc.findall('row[4]'))
    return got
t('leaf_append_remove_append', b8)
def b9():
    doc = mk(3)
    w1 = doc.iterfind('.//x[last()]')
    got = [next(w1).get('k')]
    ET.SubElement(doc[2], 'x', k='late')
    w2 = doc.iterfind('.//x[last()]')
    got.append(next(w2).get('k'))
    ET.SubElement(doc[1], 'x', k='late1')
    w3 = doc.iterfind('.//x[3]')
    got.append('|')
    got += ns(w1)
    got.append('|')
    got += ns(w2)
    got.append('|')
    got += ns(w3)
    return got
t('three_walks_three_maps', b9)
def b10():
    doc = mk(2)
    got = ns(doc.findall('row/x/..'))
    for i in range(150):
        ET.SubElement(doc, 'row', n='b%d' % i)
    got += ns(doc.findall('row[last()]')) + ns(doc.findall('row[100]/..')) + [len(doc.findall('row/..')), len(doc.findall('.//row[1]'))]
    for i in range(150):
        ET.SubElement(doc[i + 2], 'x', k='bx%d' % i)
    got += ns(doc.findall('row[last()]/x')) + ns(doc.findall('row[100]/x/..')) + [len(doc.findall('row/x/..')), len(doc.findall('.//x[1]'))]
    return got
t('many_leaves_no_search', b10)
def b11():
    doc = mk(4)
    it = doc.iterfind('row/x[2]/..')
    got = [next(it).get('n')]
    other = ET.fromstring('<a n="a"><b n="b"><c n="c"/></b><b n="b2"/></a>')
    got += ns(other.findall('.//c/..')) + ns(other.findall('b[2]'))
    got.append(next(it).get('n'))
    more = ET.fromstring('<a><b/></a>')
    ET.SubElement(more, 'b')
    got += [len(more.findall('b[last()]')), len(more.findall('b/..'))]
    got += ns(it)
    return got
t('parse_between_pulls', b11)
def b12():
    doc = mk(3)
    sub = doc[1]
    it = sub.iterfind('x[last()]')
    got = [next(it).get('k')]
    ET.SubElement(sub, 'x', k='late')
    got += ns(it)
    got += ns(sub.iterfind('x[last()]')) + ns(sub.findall('x[3]/..')) + ns(doc.findall('row/x[3]/..'))
    return got
t('start_is_owner', b12)
def b13():
    doc = mk(2)
    got = ns(doc.findall('row/..'))
    a = ET.SubElement(doc, 'row', n='A')
    b = ET.SubElement(a, 'row', n='B')
    c = ET.SubElement(b, 'row', n='C')
    got += ns(doc.findall('.//row/..')) + ns(doc.findall('.//row[1]')) + ns(doc.findall('.//row[last()]'))
    a.remove(b)
    got += ns(doc.findall('.//row/..')) + ns(doc.findall('.//row[last()]'))
    doc[0].append(b)
    got += ns(doc.findall('.//row/..')) + ns(doc.findall('.//row[last()]')) + ns(b.findall('row/..'))
    return got
t('late_chain', b13)
def b14():
    leaf = ET.Element('row', n='L')
    got = ns(leaf.findall('x/..')) + ns(leaf.findall('.//x[1]'))
    doc = mk(2)
    got += ns(doc.findall('row/..'))
    doc.append(leaf)
    got += ns(doc.findall('.//row/..')) + ns(leaf.findall('x/..'))
    ET.SubElement(leaf, 'x', k='lx')
    got += ns(leaf.findall('x/..')) + ns(doc.findall('.//x/..')) + ns(leaf.findall('.//x[1]')) + ns(doc.findall('row[3]/x[1]'))
    return got
t('start_becomes_leaf', b14)
def b15():
    doc = mk(3)
    got = []
    out2 = ET.Element('out', n='out')
    for r in doc.iterfind('row/x/..'):
        o = ET.SubElement(out2, 'o', n='o' + r.get('n'))
        for x in r.findall('x[last()]'):
            ET.SubElement(o, 'x', k='c' + x.get('k'))
        got += ns(out2.findall('o/x/..')) + ns(doc.findall('row[1]/x/..'))
    got += ns(out2.findall('o[last()]/x')) + ns(out2.findall('.//x[1]'))
    return got
t('build_out_while_walking', b15)
def b16():
    doc = mk(3)
    it = doc.iterfind('row/x/..')
    got = [next(it).get('n')]
    doc[2].clear()
    got += ns(it)
    it = doc.iterfind('.//x[1]')
    got.append(next(it).get('k'))
    new = ET.SubElement(doc, 'row', n='new')
    ET.SubElement(new, 'x', k='nx')
    del doc[1]
    got += ns(it)
    got += ns(doc.findall('.//x[1]')) + ns(doc.findall('.//x/..'))
    return got
t('leaf_then_delete', b16)
def b17():
    doc = mk(2)
    got = ns(doc.findall('row/..'))
    leaf = ET.Element('x', k='twice')
    doc[0].append(leaf)
    doc[0].append(leaf)
    got += ns(doc.findall('row/x[3]')) + ns(doc.findall('row/x[4]')) + ns(doc.findall('row/x[last()]')) + ns(doc.findall('.//x[4]/..'))
    doc[1].append(leaf)
    got += ns(doc.findall('row/x[3]')) + ns(doc.findall('row/x[last()]')) + ns(doc.findall('.//x[3]/..'))
    return got
t('leaf_twice', b17)
def b18():
    doc = mk(3)
    got = []
    for i in range(5):
        r = ET.SubElement(doc, 'row', n='r%d' % i)
        got.append(next(doc.iterfind('row[last()]')).get('n'))
        ET.SubElement(r, 'x', k='k%d' % i)
        got.append(doc.find('row[last()]/x').get('k'))
        got += ns(doc.findall('row[last()]/x/..'))
        got += ns(doc.findall('row/x/..'))[-2:]
        if i == 2:
            del doc[0]
    return got
t('loop_grow_mixed', b18)
print(chr(10).join(out))
