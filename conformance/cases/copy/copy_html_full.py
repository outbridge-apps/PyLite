# Adapted from CPython's test suite (Lib/test/test_copy.py: TestCopy.test_copy_*/test_deepcopy_*/
# test_deepcopy_memo, Lib/test/test_html.py: HtmlTests.test_escape/test_unescape).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import copy
import html
from collections import Counter, deque, OrderedDict, defaultdict, namedtuple
from dataclasses import dataclass

# ================= copy =================
# atoms come back as themselves
for atom in (1, 2 ** 70, 1.5, 'str', b'bytes', None, True, (), frozenset(), range(3)):
    assert copy.copy(atom) == atom and copy.deepcopy(atom) == atom
assert copy.copy('s') is 's' if False else copy.copy('s') == 's'
t = (1, [2])
assert copy.copy(t) is t and copy.deepcopy(t) == t and copy.deepcopy(t) is not t and copy.deepcopy(t)[1] is not t[1]

# shallow copy: a new container sharing the elements
src = [1, [2, 3], {'k': [4]}]
shallow = copy.copy(src)
assert shallow == src and shallow is not src and shallow[1] is src[1] and shallow[2] is src[2]
shallow.append(5)
assert len(src) == 3
shallow[1].append(9)
assert src[1] == [2, 3, 9]                                       # inner list shared

# deep copy: fully independent
deep = copy.deepcopy(src)
assert deep == src and deep is not src and deep[1] is not src[1] and deep[2]['k'] is not src[2]['k']
deep[1].append(10)
deep[2]['k'].append(11)
assert src[1] == [2, 3, 9] and src[2]['k'] == [4]

# every container type
d = {'a': [1], 'b': {'c': (2, [3])}}
assert copy.copy(d) == d and copy.copy(d)['a'] is d['a'] and copy.deepcopy(d)['b']['c'][1] is not d['b']['c'][1]
s = {1, 2}
assert copy.copy(s) == s and copy.copy(s) is not s and copy.deepcopy(s) == s
fs = frozenset([1])
assert copy.deepcopy(fs) == fs
cnt = Counter('aab')
assert copy.copy(cnt) == cnt and isinstance(copy.copy(cnt), Counter) and copy.deepcopy(cnt) == cnt and isinstance(copy.deepcopy(cnt), Counter)
dq = deque([[1], 2], maxlen=5)
dqc = copy.deepcopy(dq)
assert dqc == dq and dqc[0] is not dq[0] and dqc.maxlen == 5 and copy.copy(dq)[0] is dq[0]
od = OrderedDict([('b', 1), ('a', [2])])
odc = copy.deepcopy(od)
assert odc == od and list(odc) == ['b', 'a'] and isinstance(odc, OrderedDict) and odc['a'] is not od['a']
dd = defaultdict(list, {'x': [1]})
ddc = copy.deepcopy(dd)
assert ddc == dd and isinstance(ddc, defaultdict) and ddc.default_factory is list and ddc['x'] is not dd['x'] and ddc['new'] == []
P = namedtuple('P', 'x y')
p = P([1], 2)
assert copy.copy(p) == p and copy.deepcopy(p) == p and copy.deepcopy(p).x is not p.x and isinstance(copy.deepcopy(p), P)
assert copy.deepcopy(p).x == [1]

# shared references and cycles survive deepcopy (memo)
inner = [1]
outer = [inner, inner]
oc = copy.deepcopy(outer)
assert oc[0] is oc[1] and oc[0] is not inner and oc == outer
cyc = [1]
cyc.append(cyc)
cc = copy.deepcopy(cyc)
assert cc[1] is cc and cc[0] == 1 and cc is not cyc
selfd = {}
selfd['me'] = selfd
sdc = copy.deepcopy(selfd)
assert sdc['me'] is sdc and sdc is not selfd

# instances of dialect classes
class Box:
    def __init__(self, items):
        self.items = items
        self.tag = 'box'

    def __eq__(self, other):
        return isinstance(other, Box) and self.items == other.items and self.tag == other.tag

b = Box([1, [2]])
bc = copy.copy(b)
assert bc == b and bc is not b and bc.items is b.items
bd = copy.deepcopy(b)
assert bd == b and bd.items is not b.items and bd.items[1] is not b.items[1] and bd.tag == 'box'
bd.items.append(3)
assert b.items == [1, [2]]

@dataclass
class Rec:
    name: str
    vals: list

r = Rec('n', [1])
rc = copy.deepcopy(r)
assert rc == r and rc is not r and rc.vals is not r.vals and copy.copy(r).vals is r.vals

# functions, modules and types are copied by identity
def fn():
    return 1
assert copy.copy(fn) is fn and copy.deepcopy(fn) is fn and copy.deepcopy([fn])[0] is fn and copy.copy(copy) is copy
assert copy.deepcopy(int) is int and copy.copy(len) is len

# copy.Error exists and is an Exception
assert issubclass(copy.Error, Exception)
try:
    raise copy.Error('x')
except Exception as e:
    assert isinstance(e, copy.Error) and str(e) == 'x'

# ================= html =================
assert html.escape('a < b & c > d') == 'a &lt; b &amp; c &gt; d'
assert html.escape('"quoted" & \'single\'') == '&quot;quoted&quot; &amp; &#x27;single&#x27;'
assert html.escape('"quoted" & \'single\'', quote=False) == '"quoted" &amp; \'single\''
assert html.escape('"q"', False) == '"q"' and html.escape('') == '' and html.escape('plain') == 'plain' and html.escape('&amp;') == '&amp;amp;'
assert html.escape('é<') == 'é&lt;' and html.escape('<script>') == '&lt;script&gt;'
assert html.unescape('a &lt; b &amp; c &gt; d') == 'a < b & c > d' and html.unescape('&quot;x&quot; &#x27;y&#39;') == '"x" \'y\''
assert html.unescape('&#65;&#x42;&#X43;') == 'ABC' and html.unescape('&#233;') == 'é' and html.unescape('&eacute;&Eacute;') == 'éÉ'
assert html.unescape('&nbsp;') == '\xa0' and html.unescape('&copy; &euro; &mdash; &hellip;') == '© € — …'
assert html.unescape('&amp;lt;') == '&lt;' and html.unescape('&unknownentity;') == '&unknownentity;' and html.unescape('&') == '&' and html.unescape('') == ''
assert html.unescape('no entities') == 'no entities' and html.unescape('&#x1F600;') == '😀'
# CPython html.unescape: HTML5 legacy names decode without ';' (longest known prefix), upper-case AMP/LT/GT/
# QUOT/COPY/REG forms exist, out-of-range and surrogate references map to U+FFFD, NUL too
assert html.unescape('&lt') == '<' and html.unescape('&LT;') == '<' and html.unescape('&#x110000;') == '�'
assert html.unescape('&#xD800;') == '�' and html.unescape('&#0;') == '�'
assert html.unescape(html.escape('<a href="x">T&C\'s</a>')) == '<a href="x">T&C\'s</a>'
assert html.unescape(html.escape('<a href="x">T&C\'s</a>')) == '<a href="x">T&C\'s</a>'
for text in ('', 'plain', '<&>"\'', 'é☺', 'a\nb'):
    assert html.unescape(html.escape(text)) == text
try:
    html.escape(1)
    assert False
except (TypeError, AttributeError):
    pass
try:
    html.unescape(None)
    assert False
except (TypeError, AttributeError):
    pass
