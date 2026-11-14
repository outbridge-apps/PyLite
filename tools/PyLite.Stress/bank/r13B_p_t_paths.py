import xml.etree.ElementTree as ET
r = ET.fromstring('<a><b><c/><c/></b><b><c/></b></a>')
for p in ['.//c[1]', 'b/c[last()]', 'b/*[2]', './/c[2]', 'b[1]', 'b/c[1]', "b/c[@x][1]", 'b[last()]']:
    try:
        print(p, len(r.findall(p)), len(list(r.iterfind(p))))
    except Exception as e:
        print(p, type(e).__name__, e)
