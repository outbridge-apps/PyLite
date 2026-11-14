import xml.etree.ElementTree as ET
doc = ET.fromstring("<r><a>x<b>y</b>z</a><a>xyz</a><a>x<b>y<c/>q</b></a><a/><a>xy<b/>z</a><a>xy<b>z</b>w</a><a><b></b></a></r>")
for q in [".='xyz'", ".!='xyz'", ".=''", "b='y'", "b!='y'", "b=''", ".='xy'", ".='xyzw'", "b='yq'", ".='xyqz'"]:
    try:
        print(q, [ET.tostring(e) for e in doc.findall("a[" + q + "]")])
    except Exception as e:
        print(q, type(e).__name__, e)
a = doc[0]
a[0].tail = 5
for q in [".='xyz'", ".='q'", "b='y'"]:
    try:
        print(q, len(doc.findall("a[" + q + "]")))
    except Exception as e:
        print(q, type(e).__name__, e)
a[0].tail = "z"
a.text = 7
try:
    print(len(doc.findall("a[.='xyz']")))
except Exception as e:
    print(type(e).__name__, e)
a.text = "x"
a[0].text = b"y"
try:
    print(len(doc.findall("a[.='zz']")))
except Exception as e:
    print(type(e).__name__, e)
a[0].text = "y"
# positions
for q in ["a[1]", "a[7]", "a[8]", "a[2147483647]", "a[2147483648]", "a[99999999999999999999]", "a[last()]",
          "a[last()-1]", "a[last()-6]", "a[last()-7]", "a[last()-2147483646]", "a[last()-2147483647]", "a[last()-2147483648]",
          "a[last()-99999999999999999999]", "a[0]", "a[00]", "a[last()-0]", "a[last()-00]", "a[01]", "a[ 1]", "a[last() - 1]"]:
    try:
        print(q, [ET.tostring(e)[:12] for e in doc.findall(q)])
    except Exception as e:
        print(q, type(e).__name__, e)
