# Review 13 fix review: a brace protects whitespace only where a name begins; '{}' and '{*}' prefixes are wildcard paths (CPython 3.14.2 answers).
import xml.etree.ElementTree as ET
r = ET.Element('r')
for t in ['a{ b', '{ab c', 'x{u v}y', 'a', 'q']:
    ET.SubElement(r, t)
a = r[3]; e = ET.SubElement(a, ''); ET.SubElement(e, 'b')
ET.SubElement(r[0], 'zz')
for p in ['a{ b', '{ab c', 'x{u v}y', 'a [1]b', '{}a b', '{*}a b', 'a{ b/zz', './a{ b']:
    out = []
    for f in (lambda: [x.tag for x in r.findall(p)], lambda: [x.tag for x in r.iterfind(p)], lambda: (lambda z: None if z is None else z.tag)(r.find(p))):
        try: out.append(f())
        except Exception as ex: out.append(type(ex).__name__ + ":" + str(ex)[:40])
    print(repr(p), out)
