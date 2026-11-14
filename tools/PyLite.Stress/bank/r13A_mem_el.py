import xml.etree.ElementTree as ET
import copy
n = 200000

def meas(label, build):
    h0 = heap(); c0 = charged()
    keep = build()
    h1 = heap(); c1 = charged()
    print(label, 'real', (h1 - h0) // n, 'charged', (c1 - c0) // n)
    return keep

k1 = meas('Element', lambda: [ET.Element('a') for i in range(n)])
k1 = None
r = ET.Element('r')
k2 = meas('SubElement', lambda: [ET.SubElement(r, 'a') for i in range(n)])
k2 = None; r = None
r = ET.Element('r')
k3 = meas('SubElement+2attr', lambda: [ET.SubElement(r, 'a', x='1', y='2') for i in range(n)])
k3 = None; r = None
r = ET.Element('r')
def sub_attr():
    out = []
    for i in range(n):
        e = ET.SubElement(r, 'a')
        e.attrib
        out.append(e)
    return out
k4 = meas('SubElement+.attrib', sub_attr)
k4 = None; r = None
src = '<r>' + '<a x="1" y="2"/>' * n + '</r>'
k5 = meas('parsed 2attr', lambda: ET.fromstring(src))
k5 = None
src = '<r>' + '<a/>' * n + '</r>'
k6 = meas('parsed noattr', lambda: ET.fromstring(src))
base = k6
k7 = meas('copy.copy', lambda: [copy.copy(x) for x in base])
k7 = None
k8 = meas('deepcopy tree', lambda: copy.deepcopy(base))
k8 = None
k9 = meas('makeelement', lambda: [base.makeelement('q', {}) for i in range(n)])
k9 = None
import io
k10 = meas('iterparse start+end', lambda: ET.iterparse(io.StringIO(src), ('start', 'end')))
print('outcome-ish')
