# deadline: 60000
# the same document; each positional path timed on its own (5 rounds)
import xml.etree.ElementTree as ET
import time
doc = '<invoices>' + ''.join('<invoice no="%d"><head>h</head>%s</invoice>' % (i, ''.join('<line n="%d">%d</line>' % (j, i + j) for j in range(6))) for i in range(10000)) + '</invoices>'
root = ET.fromstring(doc)
doc = None
out = []
for p in ['invoice/line[1]', 'invoice/line[last()]', 'invoice/*[2]', 'invoice/line', 'invoice/line[@n]']:
    t0 = time.perf_counter()
    n = 0
    for k in range(5):
        n += len(root.findall(p))
    t1 = time.perf_counter()
    for k in range(5):
        n += sum(1 for _ in root.iterfind(p))
    t2 = time.perf_counter()
    out.append('%s %d findall %.3f iterfind %.3f' % (p, n, t1 - t0, t2 - t1))
print(' | '.join(out))
