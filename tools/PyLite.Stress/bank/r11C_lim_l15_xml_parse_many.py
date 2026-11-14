# allocmb: 64
# deadline: 20000
import xml.etree.ElementTree as ET
r = ET.fromstring("<r>" + "<c a=\"1\">t</c>" * 600000 + "</r>")
print(len(r))
