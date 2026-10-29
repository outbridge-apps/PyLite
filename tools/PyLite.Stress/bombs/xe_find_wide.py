# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a/>' * 500000 + '<z/></r>')
print(r.find('z').tag)
