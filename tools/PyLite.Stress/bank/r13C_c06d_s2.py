# deadline: 60000
import xml.etree.ElementTree as ET
import random, itertools, copy
random.seed(2)
doc = ET.fromstring('<r xmlns:p="u"><a x="1" y="2">t<b x="1">u</b><b/><p:c/></a><a x="2"><b x="3"><a/></b>v</a><c>w</c>tail</r>')
shared = doc[0][0]
doc[2].append(shared)          # shared element: in two lists
doc.append(doc[1])            # and a second copy of a subtree
toks = ['a', 'b', 'c', '*', '.', '..', '//', '/', '[', ']', '@x', '@y', '="1"', "='2'", '=', '1', '2', '0', '-1', 'last()',
        'last()-1', 'last()-0', 'last()+1', '{u}c', '{u}*', '{*}c', '{}a', 'p:c', '{', '}', '(', ')', ' ', '"', "'", 'text()',
        '.="u"', 'b="u"', '[1]', '[2]', '[last()]', '[@x]', '[@x="1"]', '[b]', '[.="t"]', '[-1]', '[0]', '[99999999999]',
        '[2147483648]', '[last()-2147483648]', '@', '!=', '!="1"', 'x', chr(0), chr(0xD800), chr(0x1F600), '\\', '|', ',',
        '::', 'child::a', 'descendant', './/', './/a', '*/', '/*']
ns = {'p': 'u', '': 'u2'}
import hashlib
H = hashlib.md5()
log = []
outcomes = {}
n = 0
for i in range(3000):
    k = random.randint(1, 7)
    path = ''.join(random.choice(toks) for j in range(k))
    for start in (doc, doc[0], shared):
        for op in range(4):
            try:
                if op == 0:
                    r = len(start.findall(path))
                elif op == 1:
                    r = start.find(path, ns)
                    r = None if r is None else r.tag
                elif op == 2:
                    r = start.findtext(path, 'D')
                else:
                    it = start.iterfind(path)
                    r = len(list(itertools.islice(it, 3)))
                key = "ok"
                log.append(repr(path) + str(op) + repr(r))
            except Exception as e:
                key = type(e).__name__
            outcomes[key] = outcomes.get(key, 0) + 1
            n += 1
print(H.hexdigest() if H.update(chr(10).join(log).encode("utf-8", "surrogatepass")) else H.hexdigest(), n, sorted(outcomes.items()))
