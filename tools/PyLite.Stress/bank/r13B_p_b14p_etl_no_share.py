# the same summary loop without the shared records
import xml.etree.ElementTree as ET
import time
out = ET.Element('out')
summary = ET.SubElement(out, 'summary')
t0 = time.perf_counter()
run = 0
for i in range(3000):
    prev = summary.find('line[last()]')
    run = (int(prev.get('run')) if prev is not None else 0) + i % 7
    ET.SubElement(summary, 'line', run=str(run))
print(len(summary), run, round(time.perf_counter() - t0, 3))
