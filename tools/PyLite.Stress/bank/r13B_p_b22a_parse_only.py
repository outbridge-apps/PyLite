# deadline: 60000
# 100,000 records with five differently named fields; one positional search over every record
import xml.etree.ElementTree as ET
import time
N = 100000
doc = '<d>' + '<r><id>1</id><nm>a</nm><qt>2</qt><pr>3</pr><cu>E</cu></r>' * N + '</d>'
root = ET.fromstring(doc)
doc = None
t0 = time.perf_counter()
a = b = c = 0
print(a, b, c, round(time.perf_counter() - t0, 3))
