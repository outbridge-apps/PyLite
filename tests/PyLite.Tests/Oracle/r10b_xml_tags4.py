import xml.etree.ElementTree as ET
import math
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:300])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__)
def nodes():
    r = ET.Element('r')
    r.append(ET.Comment(' note '))
    r.append(ET.ProcessingInstruction('php', 'echo'))
    ET.SubElement(r, 'x').text = 'v'
    r[0].tail = 'T0'
    r[1].tail = 'T1'
    return r
t('nodes itertext', lambda: list(nodes().itertext()))
t('nodes text method', lambda: ET.tostring(nodes(), method='text'))
t('nodes comment itertext', lambda: list(nodes()[0].itertext()))
t('nodes pi itertext', lambda: list(nodes()[1].itertext()))
t('nodes dot', lambda: len(nodes().findall("*[.=' note ']")))
t('nodes self', lambda: len(nodes().findall(".[x='v']")))
t('nodes findtext', lambda: nodes().findtext('x'))
t('nodes pos', lambda: [e.tag for e in nodes().findall('x[1]')])
t('nodes pos star', lambda: len(nodes().findall('*[1]')))
t('nodes desc pos', lambda: len(nodes().findall('.//x[1]')))
def nested():
    r = ET.Element('r')
    c = ET.Comment('c0')
    r.append(c)
    k = ET.SubElement(c, 'k')
    k.text = 'kt'
    k.tail = 'ktail'
    c.tail = 'ctail'
    return r
t('nested itertext', lambda: list(nested().itertext()))
t('nested text method', lambda: ET.tostring(nested(), method='text'))
t('nested tostring', lambda: ET.tostring(nested()))
t('nested iter', lambda: len(list(nested().iter())))
t('nested find k', lambda: len(nested().findall('.//k')))
def odd(tag):
    r = ET.fromstring('<r>a<c>b<d>c</d>e</c>f<c>g</c>h</r>')
    r[0].tag = tag
    return r
class NoHash:
    def __eq__(s, o):
        return True
class Plain:
    pass
def lam():
    return 1
tags = [('bslash', b'a/b'), ('bbrace', b'{x}c'), ('bstar', b'*'), ('bdot', b'.'), ('bat', b'@a'), ('bempty', b''),
        ('dict', {}), ('set', set()), ('frozenset', frozenset()), ('nohash', NoHash()), ('plain', Plain()), ('def', lam), ('len', len),
        ('type', int), ('cls', Plain), ('tuplelist', (['c'],)), ('range', range(3)), ('ellipsis', ...), ('bigint', 2 ** 80)]
for name, tag in tags:
    t('star1 ' + name, lambda: len(odd(tag).findall('*[1]')))
    t('last ' + name, lambda: len(odd(tag).findall('*[last()]')))
    t('itertext ' + name, lambda: list(odd(tag).itertext()))
def lazy():
    r = odd(5)
    r.insert(0, ET.Element('z'))
    got = []
    try:
        for e in r.iterfind('*[1]'):
            got.append(e.tag)
    except TypeError as e:
        got.append('TE')
    return got
t('lazy', lazy)
def root_pos():
    r = odd(5)
    r.tag = 7
    return len(r.findall('.[1]')), len(r.findall('c[1]'))
t('root pos', root_pos)
def text_method(tag):
    return ET.tostring(odd(tag), method='text')
t('text method int', lambda: text_method(5))
t('text method None', lambda: text_method(None))
t('lcm', lambda: [math.lcm(), math.lcm(0, 5), math.lcm(-4, 6), math.lcm(2 ** 70, 6), math.lcm(6, 2 ** 70 + 2), math.lcm(True, 7), math.lcm(0, 0), math.lcm(-2 ** 70, -2 ** 71), math.lcm(2 ** 64, 3 ** 41, 10 ** 20), math.lcm(7), math.lcm(-7), math.gcd(0, -2 ** 70), math.gcd(-2 ** 70, 0), math.gcd(0, 0), math.gcd(2 ** 200, 6 ** 100)])
print(chr(10).join(out))
