# deadline: 60000
from xml.etree import ElementTree as ET
items = "".join("<item id='%d' kind='k%d'><sub a='1'>%d</sub><name>n%d</name></item>" % (i, i % 5, i, i) for i in range(2000))
src = "<root xmlns:x='urn:x'>" + items + "</root>"
r = ET.fromstring(src)
a = len(r.findall("item"))
b = len(r.findall("item[@kind='k3']"))
c = len(r.findall(".//sub"))
d = len(r.findall("./item/name"))
e = len(r.findall("item[2]"))
f = sum(1 for _ in r.iter("sub"))
g = len(ET.tostring(r))
h = r.find("item[@id='1500']/name").text
k = len(r.findtext("item/name"))
print(a, b, c, d, e, f, g, h, k)
