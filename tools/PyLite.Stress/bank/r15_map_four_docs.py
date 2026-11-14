# deadline: 30000
import xml.etree.ElementTree as ET, time
t0 = time.perf_counter()
tot = 0
for k in range(30):
    doc = ET.fromstring('<d>' + '<rec><f>1</f><g>x</g></rec>' * 20000 + '</d>')
    tot += len(doc.findall('.//g/..'))
    doc = None
print(tot, round((time.perf_counter() - t0) * 1000))
