# deadline: 30000
import xml.etree.ElementTree as ET, time
small = ET.fromstring('<a><b><c/></b></a>')
print(len(small.findall('.//c/..')))
t0 = time.perf_counter()
doc = ET.fromstring('<d>' + ''.join('<rec><f>%d</f><f>%d</f><g>x</g><f>9</f></rec>' % (i, i + 1) for i in range(40000)) + '</d>')
print(len(doc), round((time.perf_counter() - t0) * 1000))
