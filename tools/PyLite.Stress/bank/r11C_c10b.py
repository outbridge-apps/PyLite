# deadline: 20000
import xml.etree.ElementTree as ET, io
def t(name, f):
    try:
        print(name + '=' + repr(f())[:110])
    except Exception as e:
        print(name + '!' + type(e).__name__ + ':' + str(e)[:80])
t('ns_missing', lambda: ET.fromstring('<a><b/></a>').find('p:b'))
t('ns_missing_map', lambda: ET.fromstring('<a><b/></a>').find('p:b', {'q': 'u'}))
t('ns_missing_findall', lambda: ET.fromstring('<a><b/></a>').findall('.//p:b', {}))
def tree(n):
    r = ET.Element('r')
    for i in range(n):
        ET.SubElement(r, 'c', i=str(i))
    return r
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
def ip(mode):
    out = []
    for ev, el in ET.iterparse(io.StringIO('<r>' + '<c/>' * 50 + '</r>'), events=('start', 'end')):
        out.append(ev)
        if 'clear' in mode:
            el.clear()
        if 'self' in mode and el.tag == 'r' and ev == 'start':
            el.append(el)
    return len(out)
t('ip_clear', lambda: ip('clear'))
t('ip_self', lambda: ip('self'))
t('ip_both', lambda: ip('clear self'))
