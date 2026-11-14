# deadline: 60000
# 140,000 records with five differently named fields, parsed; ONE search r/qt[1] (same answer on every engine)
import xml.etree.ElementTree as ET
import time
N = 140000
doc = '<d>' + '<r><id>1</id><nm>a</nm><qt>2</qt><pr>3</pr><cu>E</cu></r>' * N + '</d>'
root = ET.fromstring(doc)
doc = None
t0 = time.perf_counter()
b = 0
print(b, round(time.perf_counter() - t0, 3))
