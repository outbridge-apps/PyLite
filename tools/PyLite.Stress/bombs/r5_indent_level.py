# deadline: 2000
import xml.etree.ElementTree as ET
r = ET.fromstring('<r><a/></r>')
ET.indent(r, space='', level=2000000000)
print('done')
