import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:200])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:120])
def tree(n):
    r = ET.Element('r')
    for i in range(n):
        ET.SubElement(r, 'c', i=str(i))
    return r
def case(path, act, how, at=5, n=10):
    def run():
        r = tree(n)
        calls = []
        class T:
            def __eq__(s, o):
                calls.append(o if isinstance(o, str) else '?')
                if len(calls) == 1:
                    act(r)
                return False
            def __hash__(s): return 1
        r[at].tag = T()
        if how == 'findall':
            got = [e.get('i') for e in r.findall(path)]
        elif how == 'iterfind':
            got = [e.get('i') for e in r.iterfind(path)]
        elif how == 'find':
            e = r.find(path)
            got = e if e is None else e.get('i')
        elif how == 'findtext':
            got = r.findtext(path, 'dflt')
        else:
            got = [e.get('i') for e in r.iter(path)]
        return got, len(r), len(calls)
    return run
def delall(r):
    del r[:]
def delhalf(r):
    del r[3:]
def deltail(r):
    del r[7:]
def clear(r):
    r.clear()
def grow(r):
    for i in range(3):
        ET.SubElement(r, 'c', i='g%d' % i)
def insfront(r):
    r.insert(0, ET.Element('c', i='f'))
def delfront(r):
    del r[0]
def replace(r):
    r[:] = [ET.Element('c', i='n%d' % i) for i in range(12)]
acts = [('delall', delall), ('delhalf', delhalf), ('deltail', deltail), ('clear', clear), ('grow', grow), ('insfront', insfront), ('delfront', delfront), ('replace', replace)]
for an, a in acts:
    for path in ['c', './c', '*', 'c[2]', './/c', 'c/..', 'c[@i]']:
        for how in ['findall', 'iterfind', 'find']:
            t('%s %s %s' % (an, path, how), case(path, a, how))
    t('%s c findtext' % an, case('c', a, 'findtext'))
    t('%s c iter' % an, case('c', a, 'iter'))
def last_case():
    r = tree(10)
    class T:
        def __eq__(s, o):
            del r[:]
            return True
        def __hash__(s): return 1
    r[9].tag = T()
    return [e.get('i') for e in r.findall('c')], len(r)
t('del_at_last_true', last_case)
def first_case():
    r = tree(10)
    class T:
        def __eq__(s, o):
            del r[:]
            return True
        def __hash__(s): return 1
    r[0].tag = T()
    return [e.get('i') for e in r.findall('c')], len(r)
t('del_at_first_true', first_case)
def big_case():
    r = tree(100)
    class T:
        def __eq__(s, o):
            del r[:]
            return False
        def __hash__(s): return 1
    r[50].tag = T()
    return [len(r.findall('c')), len(r)]
t('del_during_find', big_case)
print(chr(10).join(out))
