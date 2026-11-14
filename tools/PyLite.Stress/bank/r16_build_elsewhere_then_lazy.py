# deadline: 60000
import xml.etree.ElementTree as ET
# add a row, search inside the new row (a build), then row[last()] from the root
r = ET.fromstring('<r>' + '<row><c/></row>' * 3000 + '</r>')
n = 0
for i in range(1500):
    row = ET.SubElement(r, 'row'); ET.SubElement(row, 'c')
    if row.find('c/..') is row: n += 1
    if next(r.iterfind('row[last()]')) is row: n += 1
print(n)
