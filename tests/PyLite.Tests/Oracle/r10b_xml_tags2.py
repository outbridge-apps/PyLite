import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:300])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:120])
def tree(n):
    r = ET.Element('r')
    for i in range(n):
        ET.SubElement(r, 'c', i=str(i)).text = 't%d' % (i % 3)
    return r
def ids(es):
    return [e.get('i') for e in es]
log = []
class T:
    def __init__(s, name, ans, act=None):
        s.name = name
        s.ans = ans
        s.act = act
        s.n = 0
    def __eq__(s, o):
        s.n += 1
        log.append(s.name + ':' + (o if isinstance(o, str) else type(o).__name__))
        if s.act is not None and s.n == 1:
            s.act()
        return s.ans
    def __hash__(s):
        return 7
def run(f):
    def g():
        del log[:]
        got = f()
        return got, log[:12], len(log)
    return g
# 1. a large parent (grouped positions) with one odd tag
def big(path, ans, how='findall', n=40, at=20, act=None):
    def f():
        r = tree(n)
        r[at].tag = T('T', ans, (lambda: act(r)) if act else None)
        if how == 'find':
            e = r.find(path)
            return None if e is None else e.get('i')
        return ids(getattr(r, how)(path)), len(r)
    return run(f)
def cut(r):
    del r[30:]
def grow(r):
    for i in range(3):
        ET.SubElement(r, 'c', i='g%d' % i)
for path in ['c[2]', 'c[last()]', 'c[last()-1]', '*[3]', 'c[25]', './/c[2]', 'c', '{*}c', '{}c', '{*}*', '{}*', './/{*}c', './/{}c', 'c[@i="20"]', "c[.='t2']"]:
    for ans in [False, True]:
        t('big %s %s' % (path, ans), big(path, ans))
        t('big %s %s find' % (path, ans), big(path, ans, 'find'))
    t('big %s cut' % path, big(path, False, act=cut))
    t('big %s grow' % path, big(path, True, act=grow))
# 2. QName tags
def qn():
    r = tree(6)
    r[2].tag = ET.QName('c')
    r[3].tag = ET.QName('{u}c')
    r[4].tag = ET.QName('u', 'c')
    return [ids(r.findall(p)) for p in ['c', './c', './/c', '{u}c', '{*}c', '{u}*', '*', 'c[2]', 'c[last()]', '{u}c[1]']], ids(r.iter('c')), ids(r.iter('{u}c')), r.findtext('{u}c')
t('qname', qn)
# 3. inert odd tags
def inert():
    r = tree(6)
    r.insert(2, ET.Comment('x'))
    r.insert(4, ET.ProcessingInstruction('p', 'q'))
    r[5].tag = None
    r[6].tag = 5
    r[7].tag = ('c',)
    return [ids(r.findall(p)) for p in ['c', './/c', '*', 'c[2]', 'c[last()]', '{*}c', '{*}*']], ids(r.iter('c')), len(list(r.iter()))
t('inert', inert)
# 4. the context node's own tag in a descendant step
def ctxnode(path, how):
    def f():
        r = tree(4)
        ET.SubElement(r[1], 'c', i='in')
        r.tag = T('R', False)
        r[1].tag = T('K', True)
        if how == 'iter':
            return ids(r.iter(path))
        return ids(getattr(r, how)(path))
    return run(f)
for path in ['.//c', './/*', './/{*}c', './/{}c', '*//c', 'c//c', './/c/..', './/c[1]']:
    t('ctx %s' % path, ctxnode(path, 'findall'))
t('ctx iter c', ctxnode('c', 'iter'))
t('ctx iter *', ctxnode('*', 'iter'))
t('ctx iter {*}c', ctxnode('{*}c', 'iter'))
# 5. child predicates
def pred(path, ans, act=None):
    def f():
        r = ET.Element('r')
        for i in range(4):
            row = ET.SubElement(r, 'row', i=str(i))
            for j in range(3):
                ET.SubElement(row, 'c', j=str(j)).text = 'x%d' % j
        r[1][1].tag = T('P', ans, (lambda: act(r)) if act else None)
        r[2][0].tag = T('Q', ans)
        return ids(r.findall(path)), [len(x) for x in r]
    return run(f)
def strip(r):
    del r[1][:]
def strip2(r):
    del r[2:]
for path in ['row[c]', "row[c='x1']", "row[c='x2']", "row[c='x0']", "row[c!='x0']", "row[{*}c='x1']", "row[c][2]", ".//row[c='x1']", "row[c='nope']"]:
    for ans in [False, True]:
        t('pred %s %s' % (path, ans), pred(path, ans))
    t('pred %s strip' % path, pred(path, False, strip))
    t('pred %s strip2' % path, pred(path, True, strip2))
# 6. a hostile text and a hostile tag in one predicate: the order of the two
def both():
    r = ET.Element('r')
    row = ET.SubElement(r, 'row')
    for j in range(3):
        ET.SubElement(row, 'c').text = 'x%d' % j
    class B:
        def __bool__(s):
            log.append('bool')
            return False
    row[0].text = B()
    row[2].tag = T('Z', True)
    return len(r.findall("row[c='x1']")), len(r.findall("row[c='zz']"))
t('both', run(both))
# 7. the eq raises
def raises(path, how):
    def f():
        r = tree(6)
        class E:
            def __eq__(s, o):
                raise ValueError('eq ' + repr(o))
            def __hash__(s):
                return 1
        r[3].tag = E()
        if how == 'iter':
            return ids(r.iter(path))
        return ids(getattr(r, how)(path))
    return f
for path in ['c', './/c', 'c[2]', '*', 'c[5]']:
    for how in ['findall', 'iterfind', 'iter']:
        t('raise %s %s' % (path, how), raises(path, how))
t('raise find c', lambda: tree(6).find('c').get('i'))
print(chr(10).join(out))
