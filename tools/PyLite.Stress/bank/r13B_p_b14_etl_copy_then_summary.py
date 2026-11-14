# ETL: records from a parsed input are appended into an output document (CPython shares them), then
# a summary section is built row by row, reading the previous row with [last()]
import xml.etree.ElementTree as ET
import time
src = ET.fromstring('<in>' + ''.join('<rec id="%d"><amt>%d</amt></rec>' % (i, i % 97) for i in range(2000)) + '</in>')
out = ET.Element('out')
body = ET.SubElement(out, 'body')
for rec in src.findall('rec'):
    if int(rec.findtext('amt')) % 2 == 0:
        body.append(rec)
summary = ET.SubElement(out, 'summary')
t0 = time.perf_counter()
run = 0
for i in range(3000):
    prev = summary.find('line[last()]')
    run = (int(prev.get('run')) if prev is not None else 0) + i % 7
    ET.SubElement(summary, 'line', run=str(run))
print(len(body), len(summary), run, round(time.perf_counter() - t0, 3))
