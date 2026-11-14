# deadline: 30000
import xml.etree.ElementTree as ET
import copy, gzip, math
from collections import deque, Counter
out = []
def t(name, f):
    try:
        out.append(name + '=' + repr(f())[:300])
    except BaseException as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:120])
# stale skip after a rename
def s1():
    root = ET.Element('root')
    x0 = ET.SubElement(root, 'x', {'id': 'x0'})
    a1 = ET.SubElement(root, 'a', {'id': 'a1'})
    it = root.iterfind('a[1]/..//*[1]')
    r = [next(it).get('id')]
    x0.tag = 'a'
    e = next(it, None)
    r.append(None if e is None else e.get('id'))
    return r
t('skip_rename', s1)
def s2():
    root = ET.Element('root')
    for i, tg in enumerate('baba'):
        ET.SubElement(root, tg, id='k%d' % i)
    it = root.iterfind('b[2]/../a[2]/..//*[2]')
    r = [next(it).get('id')]
    root[3].tag = 'b'
    r += [e.get('id') for e in it]
    return r
t('skip_rename2', s2)
def s3(path, ren, to):
    root = ET.Element('root')
    for i, tg in enumerate('aabab'):
        ET.SubElement(root, tg, id='k%d' % i)
    it = root.iterfind(path)
    r = [next(it).get('id')]
    root[ren].tag = to
    r += [e.get('id') for e in it]
    return r
for path in ['a[1]/..//*[1]', 'a[2]/..//*[2]', 'a[last()]/..//*[last()]', 'b[1]/../*[1]', 'a[1]/../a[1]', '*[1]', '*[last()]', 'a[1]/../*[1]/../a[1]', 'b[last()]/..//a[last()]']:
    for ren, to in [(0, 'b'), (1, 'b'), (2, 'a'), (4, 'a'), (3, 'c')]:
        t('s3 %s %d %s' % (path, ren, to), lambda: s3(path, ren, to))
# a run of one tag, then others
def run(n, m, path):
    root = ET.Element('root')
    for i in range(n):
        ET.SubElement(root, 'a')
    for i in range(m):
        ET.SubElement(root, 't%d' % i)
    return len(root.findall(path)), len(list(root.iterfind(path)))
for path in ['*[1]', '*[last()]', '*[2]', '*[last()-1]', './/*[1]']:
    t('run ' + path, lambda: (run(30, 20, path), run(0, 20, path), run(30, 0, path), run(1, 1, path)))
# copies of reversed(deque)
def c1(cp):
    d = deque([1, 2, 3, 4]); it = reversed(d); next(it)
    c = cp(it)
    return (c is it, next(c), next(it), list(c), list(it))
t('copy_rev', lambda: c1(copy.copy))
t('deepcopy_rev', lambda: c1(copy.deepcopy))
def c2(cp):
    d = deque([1, 2, 3, 4]); it = iter(d); next(it)
    c = cp(it)
    return (c is it, next(c), next(it), list(c), list(it))
t('copy_fwd', lambda: c2(copy.copy))
t('deepcopy_fwd', lambda: c2(copy.deepcopy))
def c3():
    d = deque([1, 2, 3]); it = reversed(d); c = copy.copy(it); list(it)
    return (list(c), list(c), next(copy.copy(it), 'end'))
t('copy_rev_done', c3)
# gzip
t('gz_omitted', lambda: int.from_bytes(gzip.compress(b'abc')[4:8], 'little'))
t('gz_data_mtime', lambda: gzip.compress('abc', mtime=-1))
t('gz_mtime_bad', lambda: gzip.compress(b'abc', mtime=-1))
t('gz_mtime_float', lambda: int.from_bytes(gzip.compress(b'abc', mtime=7.9)[4:8], 'little'))
# lcm validates past a zero
for args in [(0, 2.5), (0, 'x'), (2.5, 0), (0, 0), (3, 0, 5), (0, True), (0, None), (4, 6, 0, 2.0), (0,), ()]:
    t('lcm%r' % (args,), lambda: math.lcm(*args))
    t('gcd%r' % (args,), lambda: math.gcd(*args))
# Counter texts
t('neg_str', lambda: -Counter(a='x'))
t('pos_str', lambda: +Counter(a='x'))
t('sub_str', lambda: Counter(a=1) - Counter(b='x'))
t('sub_str2', lambda: Counter(a='x') - Counter(b=1))
t('sub_nums', lambda: [Counter(a=1) - Counter(b=-2, c=3, a=5, d=0), Counter() - Counter(a=-1.5, b=float('nan'), c=True, d=False), -Counter(a=-2, b=3, c=0, d=-0.0)])
t('add_str', lambda: Counter(a=1) + Counter(b='x'))
t('or_str', lambda: Counter(a=1) | Counter(b='x'))
t('and_str', lambda: Counter(a=1) & Counter(b='x'))
t('mc2', lambda: Counter({'a': 'x', 'b': 1}).most_common(2))
print(chr(10).join(out))
