import xml.etree.ElementTree as ET
n = 100000
root = ET.fromstring('<r>' + '<p><ph>1</ph><ph>2</ph></p>' * n + '</r>')
for path in ['p/ph[1]', 'p/ph', './/ph[1]', 'p/ph[last()]', 'p[1]', '*/ph[2]']:
    h0 = heap(); c0 = charged()
    r = root.findall(path)
    h1 = heap(); c1 = charged()
    print(path, len(r), 'charged/parent', (c1 - c0) // n, 'kept heap/parent', (h1 - h0) // n)
    r = None
h0 = heap(); c0 = charged()
it = root.iterfind('p/ph[1]')
k = 0
for x in it:
    k += 1
print('iterfind', k, (charged() - c0) // n, (heap() - h0) // n)
