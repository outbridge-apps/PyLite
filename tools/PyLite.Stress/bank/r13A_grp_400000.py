import xml.etree.ElementTree as ET
n = 400000
root = ET.fromstring('<r>' + '<p><ph>1</ph><ph>2</ph></p>' * n + '</r>')
r = root.findall('p/ph[1]')
print(len(r), r[0].text, r[-1].text)
