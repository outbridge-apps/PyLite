# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a>t</a>' * 500000 + '</r>')
print(len(r))
