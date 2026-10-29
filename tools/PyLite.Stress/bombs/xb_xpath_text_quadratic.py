# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
r = ET.fromstring('<r>' + '<a>t<b>u</b></a>' * 30000 + '</r>')
print(len(r.findall('.//*[.="zzz"]')))
