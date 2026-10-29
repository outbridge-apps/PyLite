# Adapted from CPython's test suite (Lib/test/test_re.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import re

# search vs match: match anchors at the start
m = re.search(r'\d+', 'abc123def456')
assert m is not None
assert m.group() == '123'
assert re.match(r'\d+', 'abc123') is None
assert re.match(r'a+', 'aaab').group() == 'aaa'

# findall
assert re.findall(r'[a-z]+', 'ab 12 cd') == ['ab', 'cd']
assert re.findall(r'\d', 'a1b2c3') == ['1', '2', '3']
assert re.findall(r'x', 'abc') == []

# sub
assert re.sub(r'\s+', ' ', 'a  b\tc') == 'a b c'
assert re.sub(r'\d', '#', 'a1b22', 2) == 'a#b#2'

# split
assert re.split(r'[,;]', 'a,b;c') == ['a', 'b', 'c']
assert re.split(r'(\s)', 'a b') == ['a', ' ', 'b']   # captured separator is kept

# groups
m = re.search(r'(\w+)@(\w+)\.com', 'mail user@host.com end')
assert m.group(0) == 'user@host.com'
assert m.group(1) == 'user'
assert m.group(2) == 'host'
assert m.groups() == ('user', 'host')

# character classes, quantifiers, anchors
assert re.search(r'^abc$', 'abc') is not None
assert re.search(r'^abc$', 'xabc') is None
assert re.search(r'ab?c', 'ac').group() == 'ac'
assert re.search(r'ab*c', 'abbbc').group() == 'abbbc'
assert re.search(r'a{2,3}', 'aaaa').group() == 'aaa'

# greedy vs lazy
assert re.search(r'<.+>', '<a><b>').group() == '<a><b>'
assert re.search(r'<.+?>', '<a><b>').group() == '<a>'

# alternation and escaping
assert re.search(r'cat|dog', 'hotdog').group() == 'dog'
assert re.search(r'\.', 'a.b').group() == '.'

# compiled patterns
pat = re.compile(r'\d+')
assert pat.findall('a1 b22') == ['1', '22']
assert pat.search('x9y').group() == '9'
