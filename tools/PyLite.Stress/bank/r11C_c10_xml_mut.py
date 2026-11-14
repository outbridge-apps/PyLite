# deadline: 20000
import xml.etree.ElementTree as ET, copy, itertools
def t(name, f):
    try:
        print(name + '=' + repr(f())[:110])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80])
def tree(n=5):
    r = ET.Element('r')
    for i in range(n):
        c = ET.SubElement(r, 'c', i=str(i))
        c.text = 't%d' % i
        ET.SubElement(c, 'g').text = 'g%d' % i
    return r
class EvilTag:
    def __init__(s, root, mode):
        s.root = root
        s.mode = mode
        s.n = 0
    def __eq__(s, o):
        s.n += 1
        if s.mode == 'clear':
            s.root.clear()
        elif s.mode == 'append' and s.n < 2000:
            s.root.append(ET.Element('c'))
        elif s.mode == 'remove' and len(s.root):
            s.root.remove(s.root[0])
        elif s.mode == 'raise':
            raise ValueError('eq')
        elif s.mode == 'deep' and s.n < 3:
            x = s.root
            for i in range(500):
                x = ET.SubElement(x, 'c')
        return False
    def __hash__(s):
        return 1
    def __repr__(s):
        return 'Evil'
for mode in ('clear', 'append', 'remove', 'raise', 'deep'):
    def go(mode=mode):
        r = tree()
        r[2].tag = EvilTag(r, mode)
        return [len(r.findall('c')), len(r.findall('.//g')), len(list(r.iter('c'))), r.find('c[2]') is not None, len(r.findall('*[g]')), len(r.findall(".//c[@i='3']")), len(r.findall('.//c/..')), len(list(r.itertext()))]
    t('evil_' + mode, go)
    def go2(mode=mode):
        r = tree()
        r[2].tag = EvilTag(r, mode)
        return [x.tag if isinstance(x.tag, str) else 'E' for x in r.iterfind('.//*')][:20]
    t('evil_iterfind_' + mode, go2)
    def go3(mode=mode):
        r = tree()
        r[2].tag = EvilTag(r, mode)
        return len(ET.tostring(r, encoding='unicode'))
    t('evil_tostring_' + mode, go3)
    def go4(mode=mode):
        r = tree()
        r[2].tag = EvilTag(r, mode)
        return [len(r.findall("c[.='t1']")), len(r.findall("c[g='g1']")), len(r.findall('c[last()]')), len(r.findall('{*}c'))]
    t('evil_pred_' + mode, go4)
def mut_iter():
    r = tree()
    n = 0
    for e in r.iter():
        n += 1
        if n < 1000:
            e.append(ET.Element('x'))
    return n
t('mut_iter_append', mut_iter)
def mut_iter_clear():
    r = tree()
    n = 0
    for e in r.iter():
        n += 1
        r.clear()
    return n
t('mut_iter_clear', mut_iter_clear)
def mut_iter_remove():
    r = tree()
    out = []
    for e in r.iter('c'):
        out.append(e.get('i'))
        if len(r):
            r.remove(r[0])
    return out
t('mut_iter_remove', mut_iter_remove)
def mut_iterfind():
    r = tree()
    out = []
    for e in r.iterfind('c'):
        out.append(e.get('i'))
        r.insert(0, ET.Element('c', i='n'))
        if len(out) > 50:
            break
    return out
t('mut_iterfind_insert', mut_iterfind)
def mut_iterfind_pos():
    r = tree()
    out = []
    for e in r.iterfind('c[1]'):
        out.append(e.get('i'))
        r.insert(0, ET.Element('c', i='n'))
    return out
t('mut_iterfind_pos', mut_iterfind_pos)
def mut_iterfind_deep():
    r = tree()
    out = []
    for e in r.iterfind('.//g/..'):
        out.append(e.get('i'))
        r.append(ET.fromstring('<c i="z"><g/></c>'))
        if len(out) > 30:
            break
    return out
t('mut_iterfind_parent', mut_iterfind_deep)
def mut_itertext():
    r = tree()
    out = []
    for s in r.itertext():
        out.append(s)
        if len(out) < 100:
            r.append(ET.Element('q'))
            r[-1].text = 'q'
    return len(out)
t('mut_itertext', mut_itertext)
def shared_child():
    r = ET.Element('r')
    c = ET.Element('c')
    r.append(c)
    r.append(c)
    a = ET.SubElement(r, 'a')
    a.append(c)
    return [len(r.findall('c')), len(r.findall('.//c')), r.findall('c[2]') == [c], len(r.findall('.//c/..')), len(r.findall('.//c[1]')), len(r.findall('*[last()]'))]
t('shared_child', shared_child)
def dag():
    r = ET.Element('r')
    x = r
    for i in range(30):
        y = ET.Element('n')
        x.append(y)
        x.append(y)
        x = y
    return [len(list(itertools.islice(r.iter(), 1000))), len(list(itertools.islice(r.iterfind('.//n'), 1000)))]
t('dag_iter', dag)
def dag_find():
    r = ET.Element('r')
    x = r
    for i in range(25):
        y = ET.Element('n')
        x.append(y)
        x.append(y)
        x = y
    return len(r.findall('.//n[1]'))
#t('dag_findall_pos', dag_find)
def dag_text():
    r = ET.Element('r')
    x = r
    for i in range(25):
        y = ET.Element('n')
        y.text = 'a'
        x.append(y)
        x.append(y)
        x = y
    return len(ET.tostring(r))
t('dag_tostring', dag_text)
def dag_copy():
    r = ET.Element('r')
    x = r
    for i in range(25):
        y = ET.Element('n')
        x.append(y)
        x.append(y)
        x = y
    return len(list(itertools.islice(copy.deepcopy(r).iter(), 100)))
t('dag_deepcopy', dag_copy)
def cyc_parent():
    r = ET.Element('r')
    c = ET.SubElement(r, 'c')
    c.append(r)
    return [r.find('c/r/c') is c, len(list(itertools.islice(r.iterfind('.//c'), 5)))]
t('cycle_find', cyc_parent)
def cyc_pos():
    r = ET.Element('r')
    c = ET.SubElement(r, 'c')
    c.append(r)
    return r.find('c/r/c[1]') is c
t('cycle_pos', cyc_pos)
def cyc_parent2():
    r = ET.Element('r')
    c = ET.SubElement(r, 'c')
    c.append(r)
    return r.find('c/..') is r
t('cycle_parent', cyc_parent2)
def cyc_indent():
    r = ET.Element('r')
    c = ET.SubElement(r, 'c')
    c.append(r)
    ET.indent(r)
    return 1
t('cycle_indent', cyc_indent)
def cyc_eq():
    r = ET.Element('r')
    r.append(r)
    return r == r
t('cycle_eq', cyc_eq)
def cyc_copy():
    r = ET.Element('r')
    r.append(r)
    return copy.copy(r)[0] is r
t('cycle_copy', cyc_copy)
def text_pred_mutates():
    r = tree()
    class S:
        def __init__(s): s.n = 0
    for c in r:
        c.text = 't'
    return [len(r.findall("c[.='t']")), len(r.findall(".//*[.='t']"))]
t('text_pred', text_pred_mutates)
def attrib_eq_evil():
    r = tree()
    class V:
        def __eq__(s, o):
            r.clear()
            return True
        def __hash__(s): return 1
    r[1].attrib['i'] = V()
    return len(r.findall("c[@i='1']"))
t('attrib_value_evil', attrib_eq_evil)
def attrib_key_evil():
    r = tree()
    class K:
        def __eq__(s, o):
            r.clear()
            return False
        def __hash__(s): return hash('i')
    r[1].attrib[K()] = '1'
    return [len(r.findall("c[@i]")), len(r.findall("c[@i='1']"))]
t('attrib_key_evil', attrib_key_evil)
def tag_str_sub():
    r = tree()
    r[0].tag = 5
    r[1].tag = None
    r[2].tag = ('c',)
    return [len(r.findall('c')), len(r.findall('*')), len(r.findall('{*}c')), len(list(r.iter('c')))]
t('nonstr_tags', tag_str_sub)
def text_nonstr():
    r = tree()
    r[0].text = 5
    r[1].tail = ['x']
    return [len(r.findall("c[.='5']")), len(r.findall("*[.='t3']"))]
t('nonstr_text_pred', text_nonstr)
def text_nonstr_itertext():
    r = tree()
    r[0].text = 5
    return list(r.itertext())
t('nonstr_itertext', text_nonstr_itertext)
def ext_self():
    r = tree()
    r.extend(r)
    return len(r)
t('extend_self', ext_self)
def ext_iter_mut():
    r = tree()
    r.extend(x for x in r if r.append(ET.Element('y')) is None and len(r) < 1000)
    return len(r)
t('extend_gen_mut', ext_iter_mut)
def setitem_slice_self():
    r = tree()
    r[:] = r
    return len(r)
t('slice_self', setitem_slice_self)
def setitem_slice_gen():
    r = tree()
    r[1:3] = (ET.Element('z') for _ in range(5))
    return [len(r), r[1].tag]
t('slice_gen', setitem_slice_gen)
def del_during_find():
    r = tree(100)
    class T:
        def __eq__(s, o):
            del r[:]
            return False
        def __hash__(s): return 1
    r[50].tag = T()
    return [len(r.findall('c')), len(r)]
t('del_during_find', del_during_find)
def remove_eq_evil():
    r = tree()
    class T:
        def __eq__(s, o):
            r.clear()
            return False
    x = ET.Element('x')
    try:
        r.remove(x)
    except ValueError:
        pass
    return len(r)
t('remove_missing', remove_eq_evil)
def iterparse_mut():
    import io
    out = []
    for ev, el in ET.iterparse(io.StringIO('<r>' + '<c/>' * 50 + '</r>'), events=('start', 'end')):
        out.append(ev)
        el.clear()
        if el.tag == 'r' and ev == 'start':
            el.append(el)
    return len(out)
t('iterparse_mut', iterparse_mut)
def tostring_huge_attr():
    r = ET.Element('r')
    for i in range(2000):
        r.set('a%d' % i, '"<&>' * 1000)
    return len(ET.tostring(r))
t('tostring_attr_escape', tostring_huge_attr)
def xpath_long():
    r = tree()
    return len(r.findall('/'.join(['*'] * 1000)))
t('xpath_long', xpath_long)
def xpath_many_distinct():
    r = tree()
    return sum(len(r.findall("c[@i='%d']" % i)) for i in range(5000))
t('xpath_many_distinct', xpath_many_distinct)
def xpath_huge_literal():
    r = tree()
    return len(r.findall("c[@i='" + 'x' * 1000000 + "']"))
t('xpath_huge_literal', xpath_huge_literal)
def xpath_huge_ns():
    r = tree()
    return len(r.findall('p:c', {'p': 'u' * 1000000}))
t('xpath_huge_ns', xpath_huge_ns)
def xpath_many_ns():
    r = tree()
    return len(r.findall('p:c', {('p%d' % i): 'u' for i in range(100000)}))
t('xpath_many_ns', xpath_many_ns)
