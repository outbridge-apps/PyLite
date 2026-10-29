# deadline: 20000
# allocmb: 1024
import xml.etree.ElementTree as ET
from io import StringIO
s = '<r>' + '<a>t</a>' * 500000 + '</r>'
print(sum(1 for _ in ET.iterparse(StringIO(s))))
