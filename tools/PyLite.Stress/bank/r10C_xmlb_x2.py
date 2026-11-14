# deadline: 60000
from xml.etree import ElementTree as ET
items = "".join("<item id='%d' kind='k%d'><sub a='1'>%d</sub><name>n%d</name></item>" % (i, i % 5, i, i) for i in range(2000))
src = "<root xmlns:x='urn:x'>" + items + "</root>"
r = ET.fromstring(src)
z = r.findall("item[@kind='k3']")
