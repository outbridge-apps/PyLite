# repeat: 2
import xml.etree.ElementTree as ET, io
root = ET.fromstring('<r>' + '<p a="1"><q/></p>' * 20000 + '</r>')
its = [root.iterfind('p[%d]' % (i + 1)) for i in range(50)]
for i in its[:25]:
    next(i)
x = root.findall('.//q[1]')
ev = list(ET.iterparse(io.StringIO('<r>' + '<p/>' * 20000 + '</r>'), ('start', 'end')))
print(len(x), len(ev))
