# deadline: 30000
import xml.etree.ElementTree as ET, time
doc = ET.fromstring('<d>' + '<row><f/><g/></row>' * 3000 + '</d>')
t0 = time.perf_counter()
s = 0
for row in list(doc)[:1500]:
    row.remove(row[1])
    s += len(doc.findall('.//g/..'))
print(s, round((time.perf_counter() - t0) * 1000))
