# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
s = '<r>' + ''.join('<a id="i%d"/>' % i for i in range(300000)) + '</r>'
print(len(ET.XMLID(s)[1]))
