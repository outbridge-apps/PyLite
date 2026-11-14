import xml.etree.ElementTree as ET
n = 100000
root = ET.fromstring('<r>' + '<p><ph>1</ph><ph>2</ph></p>' * n + '</r>')
h0 = heap(); c0 = charged()
r = root.findall('p/ph[1]/..')
print('p/ph[1]/..', len(r), 'charged/parent', (charged() - c0) // n)
