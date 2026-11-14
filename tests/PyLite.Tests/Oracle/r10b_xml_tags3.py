import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:300])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__)
class T:
    def __eq__(s, o):
        return True
    def __hash__(s):
        return 7
class G:
    def __eq__(s, o):
        return True
    def __hash__(s):
        return 7
    def __getitem__(s, k):
        return 'c'
def doc(tag):
    r = ET.fromstring('<r>a<c>b<d>c</d>e</c>f<c>g</c>h</r>')
    r[0].tag = tag
    return r
tags = [('T', T()), ('None', None), ('int', 5), ('tuple', ('c',)), ('bytes', b'c'), ('list', ['c']), ('qname', ET.QName('c')), ('comment', ET.Comment), ('pi', ET.PI), ('float', 1.5), ('bool', True), ('G', G())]
for name, tag in tags:
    t('itertext ' + name, lambda: list(doc(tag).itertext()))
    t('itertext self ' + name, lambda: list(doc(tag)[0].itertext()))
    t('text pred ' + name, lambda: len(doc(tag).findall(".[c='bce']")))
    t('dot pred ' + name, lambda: len(doc(tag).findall("*[.='bce']")))
    t('pos star ' + name, lambda: len(doc(tag).findall('*[1]')))
    t('pos star 2 ' + name, lambda: len(doc(tag).findall('*[2]')))
    t('pos c ' + name, lambda: len(doc(tag).findall('c[1]')))
    t('pos last ' + name, lambda: len(doc(tag).findall('*[last()]')))
    t('pos find ' + name, lambda: doc(tag).find('*[1]') is None)
    t('pos desc ' + name, lambda: len(doc(tag).findall('.//*[1]')))
    t('iter c ' + name, lambda: len(list(doc(tag).iter('c'))))
    t('findall c ' + name, lambda: len(doc(tag).findall('c')))
    t('findall ./c ' + name, lambda: len(doc(tag).findall('./c')))
    t('findtext c ' + name, lambda: doc(tag).findtext('c'))
def real_comment():
    r = ET.Element('r')
    r.text = 'a'
    c = ET.Comment('note')
    c.tail = 'b'
    r.append(c)
    p = ET.PI('t', 'x')
    p.tail = 'c'
    r.append(p)
    e = ET.SubElement(r, 'e')
    e.text = 'd'
    return list(r.itertext()), list(c.itertext()), len(r.findall(".[e='d']")), len(r.findall("*[.='note']")), ET.tostring(r)
t('real comment', real_comment)
print(chr(10).join(out))
