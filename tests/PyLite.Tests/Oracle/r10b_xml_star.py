import xml.etree.ElementTree as ET
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:400])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:120])
def ids(es):
    return [e.get('i') for e in es]
def tree(tags):
    r = ET.Element('r')
    for i, tg in enumerate(tags):
        ET.SubElement(r, tg, i=str(i))
    return r
shapes = {'empty': '', 'one': 'a', 'same3': 'aaa', 'same20': 'a' * 20, 'mixed': 'aabab', 'mixed20': 'ab' * 10, 'lastodd': 'aaaab', 'firstodd': 'baaaa', 'three': 'abcabcabc', 'same40': 'a' * 40, 'mixed40': 'aaab' * 10}
preds = ['[1]', '[2]', '[3]', '[last()]', '[last()-1]', '[last()-2]', '[20]', '[21]', '[last()-19]', '[last()-20]', '[1][2]', '[2][1]', '[last()][1]', '[1][@i]', '[@i][1]', '[2][@i="1"]', '[@i="1"][2]', '[last()][last()]']
for name in shapes:
    for p in preds:
        for head in ['*', './*', './/*', '*/..', '{*}*', '{}*']:
            path = head + p if head != '*/..' else '*' + p + '/..'
            t('%s %s' % (name, path), lambda: (ids(tree(shapes[name]).findall(path)), ids(tree(shapes[name]).iterfind(path))))
        t('%s find *%s' % (name, p), lambda: (lambda e: None if e is None else e.get('i'))(tree(shapes[name]).find('*' + p)))
# two levels: the skip per owner
def deep():
    r = ET.Element('r')
    n = 0
    for tags in ['aaa', 'ab', '', 'bbbb', 'a']:
        g = ET.SubElement(r, 'g', i='g%d' % n)
        n += 1
        for tg in tags:
            ET.SubElement(g, tg, i=str(n))
            n += 1
    return r
for path in ['*/*[1]', '*/*[last()]', '*/*[2]', '*[2]/*[last()]', '*[last()]/*[1]', './/*[last()]', '*/*[last()-1]', 'g/*[3]', '*[1]/*[3]/..']:
    t('deep ' + path, lambda: (ids(deep().findall(path)), ids(deep().iterfind(path))))
# the tree changes between an iterfind's items
def live(path, act):
    def f():
        r = tree('aaaaa')
        got = []
        for e in r.iterfind(path):
            got.append(e.get('i'))
            act(r)
            if len(got) >= 12:
                break
        return got, len(r)
    return f
def app(r):
    ET.SubElement(r, 'a', i='n%d' % len(r))
def appb(r):
    ET.SubElement(r, 'b', i='n%d' % len(r))
def pop(r):
    del r[-1]
def ren(r):
    r[0].tag = 'b'
def ren_last(r):
    r[-1].tag = 'b'
for path in ['*[1]', '*[last()]', '*[2]', '*[last()-1]', '*']:
    for act in [app, appb, pop, ren, ren_last]:
        t('live %s %s' % (path, act.__name__), live(path, act))
# the add-a-row loop
def loop(n, path):
    r = ET.Element('t')
    got = []
    for i in range(n):
        ET.SubElement(r, 'row', i=str(i))
        got.append(r.find(path).get('i'))
    return got[-3:], len(got)
t('loop *[last()]', lambda: loop(300, '*[last()]'))
t('loop *[1]', lambda: loop(300, '*[1]'))
t('loop *[last()-1]', lambda: loop(300, '*[last()]/../*[1]'))
print(chr(10).join(out))
