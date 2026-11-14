import xml.etree.ElementTree as ET
import copy

# 1: an element in one list before the walk, appended to a second parent during a lazy iterfind
r = ET.fromstring('<r><a><x/></a><a><x/></a></r>')
b = ET.Element('b')
got = []
for x in r.iterfind('a/x[1]'):
    got.append(x)
    b.append(r[1][0])
print("share", len(got))

# 2: copy.copy of an element during the walk (its children now in two lists)
r = ET.fromstring('<r><a><x/></a><a><x/></a></r>')
got = []
keep = []
for x in r.iterfind('a/x[1]'):
    got.append(x)
    keep.append(copy.copy(r[1]))
print("copy", len(got))

# 3: descendant axis, copy.copy of the root during the walk
r = ET.fromstring('<r><a><x/><x/></a><a><x/><x/></a><a><x/></a></r>')
got = []
for x in r.iterfind('.//x[1]'):
    got.append(x)
    keep.append(copy.copy(r))
print("desc", len(got))

# 4: [last()] with copy of a parent
r = ET.fromstring('<r><a><x/><x/></a><a><x/><x/></a></r>')
got = []
for x in r.iterfind('a/x[last()]'):
    got.append(x)
    keep.append(copy.copy(r[1]))
print("last", len(got))
