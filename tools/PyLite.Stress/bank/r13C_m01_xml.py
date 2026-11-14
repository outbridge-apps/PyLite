import xml.etree.ElementTree as ET
import copy
N = 100000
def meas(name, build):
    h0 = heap(); c0 = charged()
    keep = build()
    h1 = heap(); c1 = charged()
    print(name, 'real/obj', (h1 - h0) // N, 'charged/obj', (c1 - c0) // N)
    return keep

k1 = meas('Element-noattr', lambda: [ET.Element('a') for i in range(N)])
def touch():
    l = [ET.Element('a') for i in range(N)]
    h0 = heap(); c0 = charged()
    for e in l:
        e.attrib
    print('attrib-touch', 'real/obj', (heap() - h0) // N, 'charged/obj', (charged() - c0) // N)
    h0 = heap(); c0 = charged()
    for e in l:
        e.get('x'); e.keys(); e.items()
    print('get/keys/items-after', 'real/obj', (heap() - h0) // N, 'charged/obj', (charged() - c0) // N)
    return l
k2 = touch()
def getonly():
    l = [ET.Element('a') for i in range(N)]
    h0 = heap(); c0 = charged()
    for e in l:
        e.get('x'); e.keys(); e.items(); len(e.attrib) if False else 0
    print('get/keys/items-fresh', 'real/obj', (heap() - h0) // N, 'charged/obj', (charged() - c0) // N)
    return l
k3 = getonly()
k4 = meas('Element-1attr', lambda: [ET.Element('a', x='1') for i in range(N)])
k5 = meas('Element-set', lambda: [ET.Element('a') for i in range(N)])
for e in k5:
    pass
def sets():
    l = [ET.Element('a') for i in range(N)]
    h0 = heap(); c0 = charged()
    for e in l:
        e.set('x', '1')
    print('set-1', 'real/obj', (heap() - h0) // N, 'charged/obj', (charged() - c0) // N)
    return l
k6 = sets()
root = ET.Element('r')
k7 = meas('SubElement', lambda: [ET.SubElement(root, 'c') for i in range(N)])
doc = '<r>' + '<c a="1" b="2" c="3">t</c>' * N + '</r>'
k8 = meas('parsed-3attr', lambda: ET.fromstring(doc))
k9 = meas('deepcopy-parsed', lambda: copy.deepcopy(k8))
k10 = meas('copy-each', lambda: [copy.copy(e) for e in k8])
k11 = meas('makeelement', lambda: [root.makeelement('z', {}) for i in range(N)])
import io
def ip():
    h0 = heap(); c0 = charged()
    ev = list(ET.iterparse(io.StringIO(doc), events=('start', 'end')))
    print('iterparse-list(2/elem)', 'real/elem', (heap() - h0) // N, 'charged/elem', (charged() - c0) // N)
    return ev
k12 = ip()
def ip2():
    h0 = heap(); c0 = charged()
    it = ET.iterparse(io.StringIO(doc), events=('start', 'end'))
    first = next(it)
    print('iterparse-pending', 'real/elem', (heap() - h0) // N, 'charged/elem', (charged() - c0) // N)
    return it
k13 = ip2()
def fa():
    h0 = heap(); c0 = charged()
    l = [k8.findall('c[@a]') for i in range(3)]
    print('findall-attr x3', 'real/elem', (heap() - h0) // N, 'charged/elem', (charged() - c0) // N)
    return l
k14 = fa()
def its():
    h0 = heap(); c0 = charged()
    l = [k8.iterfind('.//c[2]') for i in range(20)]
    for x in l:
        next(x)
    print('20 iterfind c[2] pending', 'real/elem', (heap() - h0) // N, 'charged/elem', (charged() - c0) // N)
    return l
k15 = its()
def xmlid():
    h0 = heap(); c0 = charged()
    d = ET.XMLID('<r>' + ''.join('<c id="i%d"/>' % i for i in range(N)) + '</r>')
    print('XMLID', 'real/elem', (heap() - h0) // N, 'charged/elem', (charged() - c0) // N)
    return d
k16 = xmlid()
