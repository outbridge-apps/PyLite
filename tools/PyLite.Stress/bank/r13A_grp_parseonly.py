# deadline: 30000
import xml.etree.ElementTree as ET
n = 200000
root = ET.fromstring('<r>' + '<p><ph>1</ph><ph>2</ph></p>' * n + '</r>')
r = [root[0][0]]
print(len(r), r[0].text, r[-1].text)
