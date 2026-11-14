# deadline: 60000
import xml.etree.ElementTree as ET
# move rows from src to dst: src is stale after each remove; dst only got a leaf
src = ET.fromstring('<s>' + '<row/>' * 1500 + '</s>')
dst = ET.fromstring('<d>' + '<old><x/></old>' * 3000 + '</d>')
n = 0
for i in range(1499):
    e = src[0]; src.remove(e); dst.append(e)
    if src.find('row/..') is src: n += 1
    if next(dst.iterfind('row[last()]')) is e: n += 1
print(n)
