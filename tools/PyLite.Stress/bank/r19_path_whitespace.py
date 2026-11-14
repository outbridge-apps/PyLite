# Review 13, row 35: whitespace in a path is a step of its own, the children tagged '' (CPython 3.14.2 answers).
import xml.etree.ElementTree as ET
r = ET.fromstring('<r><a><b>1</b></a><a v="x"><b>2</b></a><b>3</b></r>')
e0 = ET.SubElement(r, ''); ET.SubElement(e0, 'b').text = '4'
ET.SubElement(r[0], '')
def show(p, ns=None):
    try:
        res = [x.tag + ':' + str(x.text) for x in r.iterfind(p, ns)]
    except Exception as ex:
        res = type(ex).__name__ + ' ' + str(ex)
    try:
        fa = [x.tag for x in r.findall(p, {'q': 'u'})]
    except Exception as ex:
        fa = type(ex).__name__ + ' ' + str(ex)
    try:
        f = r.find(p)
        f = None if f is None else f.tag
    except Exception as ex:
        f = type(ex).__name__ + ' ' + str(ex)
    print(repr(p), res, fa, f)
for p in [' ', '  ', '\t', 'a b', ' b', 'b ', 'a/ b', 'a /b', 'a / b', './/b ', ' .//b', 'a[ b ]', "a[ @v = 'x' ]", "a[@v='x' ]", 'a[ 1 ]', '*[ b ]', ' a', 'a\n', 'a  b', '. ', ' .', '.. ', 'a/ ', ' /b', 'a//b ', '{u}a b', 'q:a b']:
    show(p)
