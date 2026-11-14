import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:300])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:200])
def mk(n=4, tag='row'):
    doc = ET.Element('doc')
    for i in range(n):
        r = ET.SubElement(doc, tag, n=str(i))
        ET.SubElement(r, 'x', k='x%d' % i)
        ET.SubElement(r, 'x', k='y%d' % i)
    return doc
def ns(items):
    return [e.get('n') or e.get('k') for e in items]

# add a row between pulls of row[last()]
def a1():
    doc = mk(2)
    it = doc.iterfind('row[last()]')
    got = [next(it).get('n')]
    doc.append(doc[0])
    got += ns(it)
    return got
t('last_then_append_first', a1)
def a2():
    doc = mk(2)
    it = doc.iterfind('row[last()]')
    got = [next(it).get('n')]
    ET.SubElement(doc, 'row', n='new')
    got += ns(it)
    return got
t('last_then_append_new', a2)
def a3():
    doc = mk(3)
    it = doc.iterfind('row[2]')
    ET.SubElement(doc, 'row', n='new')
    doc.insert(0, ET.Element('row', n='front'))
    return ns(it)
t('second_insert_before_first_pull', a3)
def a4():
    doc = mk(3)
    it = doc.iterfind('row[2]')
    got = [next(it).get('n')]
    doc.insert(0, ET.Element('row', n='front'))
    got += ns(it)
    return got
t('second_then_insert_front', a4)
def a5():
    doc = mk(4)
    it = doc.iterfind('row[1]')
    got = [next(it).get('n')]
    del doc[0]
    got += ns(it)
    return got
t('first_then_del_first', a5)
def a6():
    doc = mk(4)
    it = doc.iterfind('row[last()]')
    got = [next(it).get('n')]
    doc[0].tag = 'other'
    doc[3].tag = 'other'
    got += ns(it)
    return got
t('last_then_rename', a6)
def a7():
    doc = mk(4)
    it = doc.iterfind('row[2]')
    doc[0].tag = 'other'
    return ns(it)
t('second_rename_before_pull', a7)
def a8():
    doc = mk(4)
    it = doc.iterfind('row[2]/x')
    got = [next(it).get('k')]
    doc[1].append(ET.Element('x', k='late'))
    got += ns(it)
    return got
t('second_x_then_append', a8)
def a9():
    doc = mk(4)
    it = doc.iterfind('row/x[2]')
    got = [next(it).get('k')]
    doc[1].insert(0, ET.Element('x', k='ins1'))
    doc[2].insert(0, ET.Element('x', k='ins2'))
    got += ns(it)
    return got
t('x2_then_insert', a9)
def a10():
    doc = mk(4)
    it = doc.iterfind('row/x[last()]')
    got = [next(it).get('k')]
    doc[1][1].tag = 'z'
    doc[2].remove(doc[2][1])
    got += ns(it)
    return got
t('xlast_then_rename_remove', a10)
def a11():
    doc = mk(4)
    it = doc.iterfind('.//x[1]')
    got = [next(it).get('k')]
    doc[1].insert(0, ET.Element('x', k='ins1'))
    doc[2].clear()
    got += ns(it)
    return got
t('desc_x1_then_change', a11)
def a12():
    doc = mk(4)
    it = doc.iterfind('row[last()]')
    got = [next(it).get('n')]
    other = ET.Element('other')
    other.append(doc[0])
    doc.append(doc[0])
    got += ns(it)
    return got
t('last_then_share_and_append', a12)
def a13():
    doc = mk(3)
    doc.append(doc[0])
    return ns(doc.findall('row[1]')), ns(doc.findall('row[last()]')), ns(doc.findall('row[2]')), ns(doc.iterfind('row[4]'))
t('shared_child_positions', a13)
def a14():
    doc = mk(3)
    return ns(doc.findall('row/x/../../row[last()]')), ns(doc.findall('row/../row[2]')), ns(doc.findall('row[2]/x/..')), ns(doc.findall('.//x/..[2]'))
t('parent_axis_positions', a14)
def a15():
    doc = mk(5)
    got = []
    for r in doc.iterfind('row[last()]'):
        got.append(r.get('n'))
        if len(got) < 4:
            ET.SubElement(doc, 'row', n='g%d' % len(got))
    return got
t('for_last_grow', a15)
def a16():
    doc = mk(5)
    got = []
    for r in doc.iterfind('row[2]'):
        got.append(r.get('n'))
        if len(got) < 4:
            del doc[0]
    return got
t('for_second_shrink', a16)
def a17():
    doc = mk(5)
    got = []
    for r in doc.iterfind('row'):
        got.append(r.get('n'))
        if len(got) == 2:
            doc[3].tag = 'zz'
            doc.insert(0, ET.Element('row', n='front'))
    return got
t('for_plain_change', a17)
def a18():
    doc = mk(3)
    it1 = doc.iterfind('row[last()]')
    it2 = doc.iterfind('row[1]')
    a = next(it1).get('n')
    ET.SubElement(doc, 'row', n='late')
    b = next(it2).get('n')
    doc.insert(0, ET.Element('row', n='front'))
    return a, b, ns(it1), ns(it2)
t('two_walks', a18)
def a19():
    doc = mk(3)
    it = doc.iterfind('row[last()]/x[1]')
    got = [next(it).get('k')]
    ET.SubElement(doc, 'row', n='late').append(ET.Element('x', k='lx'))
    got += ns(it)
    return got
t('last_x1_then_append', a19)
def a20():
    doc = mk(4)
    it = doc.iterfind('row[3]')
    doc[2].tag = 'q'
    got = ns(it)
    it = doc.iterfind('row[3]')
    doc[2].tag = 'row'
    got += ns(it)
    return got
t('rename_before_pull_3', a20)
def a21():
    doc = mk(3)
    got = []
    for i in range(6):
        ET.SubElement(doc, 'row', n='a%d' % i)
        got.append(next(doc.iterfind('row[last()]')).get('n'))
        got.append(list(doc.iterfind('row[last()-1]'))[0].get('n'))
        got.append(doc.find('*[last()]').get('n'))
    return got
t('loop_grow', a21)
def a22():
    doc = mk(4)
    it = doc.iterfind('row[last()]')
    doc[3].tag = 'q'
    doc[3].tag = 'row'
    doc[0].tag = 'q'
    got = [next(it).get('n')]
    doc[3].tag = 'q'
    got += ns(it)
    return got
t('rename_only', a22)
def a23():
    doc = mk(4)
    it = doc.iterfind('*[2]')
    got = [next(it).get('n')]
    doc.insert(0, ET.Element('k', n='front'))
    got += ns(it)
    it = doc.iterfind('*[last()]')
    got.append(next(it).get('n'))
    doc.append(doc[1])
    got += ns(it)
    return got
t('star_positions', a23)
print(chr(10).join(out))
