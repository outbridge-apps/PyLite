import xml.etree.ElementTree as ET

# move each processed record to an output tree while a lazy iterfind walks the input
src = ET.fromstring('<r><rec><f>1</f><s><f>1b</f></s></rec><rec><f>3</f><f>4</f></rec><rec><f>5</f></rec></r>')
out = ET.Element('out')
got = []
for f in src.iterfind('.//f[1]'):
    got.append(f.text)
    rec = src[0]
    src.remove(rec)          # detach the record being read
    ET.SubElement(out, 'done')   # any insertion anywhere
print("detach", got)

# same, child axis two steps: rec/f[last()]
src = ET.fromstring('<r><rec><f>1</f><f>2</f></rec><rec><f>3</f><f>4</f></rec></r>')
got = []
recs = list(src)
for f in src.iterfind('rec/f[last()]'):
    got.append(f.text)
print("plain", got)
