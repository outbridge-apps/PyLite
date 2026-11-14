# deadline: 60000
# one attribute per element, parsed from bytes read in; the document text is built once
import xml.etree.ElementTree as ET
N = 300000
doc = '<d>' + '<r k="1"/>' * N + '</d>'
root = ET.fromstring(doc)
doc = None
print(len(root), root[0].get('k'))
