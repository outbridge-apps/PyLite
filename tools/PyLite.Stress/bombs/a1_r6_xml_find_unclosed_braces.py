# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
root = ET.Element('r')
try:
    root.find('{' * 150000)
except SyntaxError:
    pass
