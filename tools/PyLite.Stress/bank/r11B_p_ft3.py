import time
import xml.etree.ElementTree as ET
ln = ET.fromstring("<Line no='1'><Item>A</Item><Qty>3</Qty><Price>1.50</Price></Line>")
o = ET.fromstring("<Order><Lines><Line><Item>A</Item></Line><Line><Item>B</Item></Line><Line><Qty>4</Qty></Line></Lines></Order>")
N = 50000

t = time.perf_counter()
x = 0
for i in range(N):
    x += len(list(o.iterfind("Lines/Line")))
print(x, round((time.perf_counter() - t) * 1000))
