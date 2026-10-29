# Adapted from CPython's test suite (Lib/test/test_re.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import re

# named groups
m = re.search(r'(?P<user>\w+)@(?P<host>\w+)', 'a user@host b')
assert m.group('user') == 'user'
assert m.group('host') == 'host'
assert m.groupdict() == {'user': 'user', 'host': 'host'}

# span / start / end
m = re.search(r'\d+', 'ab123cd')
assert m.start() == 2
assert m.end() == 5
assert m.span() == (2, 5)

# finditer
spans = [m.span() for m in re.finditer(r'\d+', 'a1b22c333')]
assert spans == [(1, 2), (3, 5), (6, 9)]
texts = [m.group() for m in re.finditer(r'[a-z]+', 'ab1cd2')]
assert texts == ['ab', 'cd']

# flags: ignorecase, multiline, dotall
assert re.search(r'hello', 'HELLO', re.IGNORECASE) is not None
assert re.search(r'(?i)hello', 'HeLLo') is not None
assert re.findall(r'^\w+', 'one\ntwo\nthree', re.MULTILINE) == ['one', 'two', 'three']
assert re.search(r'a.b', 'a\nb') is None
assert re.search(r'a.b', 'a\nb', re.DOTALL) is not None

# sub with backreferences
assert re.sub(r'(\w+) (\w+)', r'\2 \1', 'hello world') == 'world hello'
assert re.sub(r'(?P<w>\w+)!', r'\g<w>?', 'stop! go!') == 'stop? go?'

# non-capturing groups do not count
m = re.search(r'(?:ab)+(c)', 'ababc')
assert m.groups() == ('c',)
assert m.group(1) == 'c'

# optional group that did not participate is None
m = re.search(r'(a)(b)?', 'a')
assert m.groups() == ('a', None)

# alternation with groups
m = re.search(r'(cat|dog)food', 'dogfood')
assert m.group(1) == 'dog'

# word boundaries
assert re.findall(r'\bcat\b', 'cat catalog concat cat') == ['cat', 'cat']

# lookahead / lookbehind
assert re.findall(r'\d+(?=px)', '10px 20em 30px') == ['10', '30']
assert re.findall(r'(?<=\$)\d+', '$5 and $10') == ['5', '10']

# split with maxsplit; fullmatch semantics via anchors
assert re.split(r',', 'a,b,c', 1) == ['a', 'b,c']
assert re.match(r'\d+$', '123') is not None
assert re.match(r'\d+$', '123x') is None

# escaping metacharacters with re.escape? use literal escapes instead
assert re.search(r'\(\)', 'f()').group() == '()'
assert re.search(r'\[1\]', 'a[1]').group() == '[1]'

# quantifier braces and character-class ranges
assert re.findall(r'[0-9a-f]{2}', 'ff0z1c') == ['ff', '1c']
assert re.search(r'^[^abc]+$', 'xyz') is not None
assert re.search(r'^[^abc]+$', 'xya') is None

# ---- Match.expand / lastindex / lastgroup ----
assert re.match(r'(a)(b)', 'ab').expand(r'\2\1') == 'ba'
assert re.match(r'(?P<x>a)', 'a').expand(r'<\g<x>>') == '<a>'
assert re.match(r'ab', 'ab').expand(r'[\g<0>]') == '[ab]'
assert re.match(r'(a)(b)?', 'a').expand(r'[\2]') == '[]'     # a group that did not participate is ''
assert re.match(r'(a)(b)', 'ab').lastindex == 2
assert re.match(r'((a)(b))', 'ab').lastindex == 1            # the outer group closes last
assert re.match(r'(a)|(b)', 'b').lastindex == 2
assert re.match(r'a', 'a').lastindex is None
assert re.match(r'(?P<n>a)', 'a').lastgroup == 'n' and re.match(r'(a)', 'a').lastgroup is None

# ---- pattern syntax .NET does not share with Python, rewritten by the translator ----

# possessive quantifiers (3.11): an atomic group, never giving back what it matched
assert re.search(r'a*+b', 'aaab').group() == 'aaab'
assert re.match(r'a*+a', 'aaa') is None and re.match(r'a*a', 'aaa').group() == 'aaa'
assert re.search(r'a++b', 'aaab').group() == 'aaab'
assert re.match(r'a?+a', 'a') is None
assert re.search(r'a{1,3}+b', 'aab').group() == 'aab'
assert re.search(r'[ab]*+c', 'abac').group() == 'abac'
assert re.search(r'(ab)*+c', 'ababc').group(1) == 'ab'
assert re.match(r'(?:ab|a)*+b', 'aba') is None
assert re.search(r'a*?b', 'aaab').group() == 'aaab'      # the lazy form stays lazy

# {,n} is {0,n}; .NET alone would read the brace as a literal
assert re.findall(r'a{,3}', 'aaaa') == ['aaa', 'a', '']
assert re.match(r'a{,2}', 'aaa').group() == 'aa' and re.match(r'a{,}', 'aaa').group() == 'aaa'
assert re.match(r'a{2,3}', 'aaaa').group() == 'aaa' and re.match(r'a{2,}', 'aaa').group() == 'aaa'
assert re.findall(r'x{}', 'x{}') == ['x{}'] and re.findall(r'x{a}', 'x{a}') == ['x{a}']

# \N{NAME} in a pattern (3.8), off the same table the lexer uses for string literals
assert ord(re.search(r'\N{DEGREE SIGN}', '5°').group()) == 176
assert [ord(c) for c in re.findall(r'[\N{DEGREE SIGN}x]', 'x°y')] == [120, 176]
assert re.findall(r'\N{FULL STOP}', 'a.b') == ['.']      # no metacharacter leaks out of the name
assert bool(re.search(r'\N{NUL}', '\x00'))               # an alias resolves too
for bad in (r'\N{NO SUCH NAME}', r'\N{DEGREE', r'\Nx'):
    try:
        re.compile(bad)
        assert False, bad
    except re.error:
        pass
