# deadline: 30000
import math, itertools
import xml.etree.ElementTree as ET
from collections import deque, Counter
big = math.factorial(2500)
s = 0
for i in range(1, 2000):
    s += math.gcd(big, i) + (math.lcm(big, i) & 1) + (math.lcm(i, big) & 1) + math.lcm(i, i + 1, i + 2) % 7
print(s, math.lcm(*range(1, 300)) % 10 ** 9, math.lcm(big, 2 ** 9000 + 1) % 10 ** 9, math.lcm(2 ** 9000 + 1, big) == math.lcm(big, 2 ** 9000 + 1))
r = ET.Element('r')
r.append(ET.Comment(' note '))
r.append(ET.ProcessingInstruction('php', 'echo'))
ET.SubElement(r, 'x').text = 'v'
r[0].tail = 'T0'
r[1].tail = 'T1'
print(list(r.itertext()), list(r[0].itertext()), list(r[1].itertext()), ET.tostring(r, method='text'))
def t(f):
    try:
        return f()
    except Exception as e:
        return type(e).__name__ + ': ' + str(e)
def tagged(tag):
    e = ET.fromstring('<r><c/><c/><d/></r>')
    e[0].tag = tag
    return e
print(t(lambda: len(r.findall('*[1]'))))
print(t(lambda: len(r.findall('x[1]'))))
for tag in (5, None, {}, b'c', b'a/b', (1, 2)):
    print(repr(tag), '|', t(lambda: len(tagged(tag).findall('*[1]'))), '|', t(lambda: len(tagged(tag).findall('c[last()]'))), '|', t(lambda: len(tagged(tag).findall('d[1]'))))
