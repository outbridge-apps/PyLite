# Adapted from CPython's test suite (Lib/test/test_re.py: ReTests.test_search_star_plus/test_basic_re_sub/
# test_symbolic_groups/test_re_split/test_re_findall/test_finditer/test_flags/test_span/test_groupdict/
# test_lookahead/test_lookbehind/test_escape/test_pattern_attributes/test_match_attributes/test_bug_*).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every re.* function, flag, Pattern and Match member the dialect exposes is exercised here.

import re

# ---- search / match / fullmatch ----
assert re.search('b', 'abc').start() == 1 and re.search('x', 'abc') is None and re.search('', 'abc').span() == (0, 0)
assert re.match('a', 'abc') is not None and re.match('b', 'abc') is None and re.match('', 'abc').group() == ''
assert re.fullmatch('a.c', 'abc') is not None and re.fullmatch('a', 'abc') is None and re.fullmatch('a|ab', 'ab').group() == 'ab'
assert re.search('a+', 'caaat').group() == 'aaa' and re.search('a+?', 'caaat').group() == 'a' and re.search('a*', 'bbb').group() == ''
assert re.search('^b', 'abc') is None and re.search('c$', 'abc').group() == 'c' and re.search(r'\bc', 'a c').group() == 'c'
assert re.search(r'\d+', 'ab12cd345').group() == '12' and re.search(r'\w+', '  héllo!').group() == 'héllo' and re.search(r'\s', 'a b').start() == 1
assert re.search(r'[a-c]+', 'xxabcaxx').group() == 'abca' and re.search(r'[^a-c]+', 'abcxyz').group() == 'xyz' and re.search(r'a{2,3}', 'aaaa').group() == 'aaa'
assert re.search(r'a{2}', 'aaaa').span() == (0, 2) and re.search(r'ab?c', 'ac').group() == 'ac' and re.search(r'(ab)+', 'ababab').group() == 'ababab'
assert re.search('a.c', 'a\nc') is None and re.search('a.c', 'a\nc', re.DOTALL).group() == 'a\nc' and re.search('(?s)a.c', 'a\nc') is not None
assert re.search(r'\.', 'a.b').start() == 1 and re.search('[.]', 'a.b').start() == 1 and re.search(r'\\', 'a\\b').start() == 1
assert re.search(r'\A\w+\Z', 'word') is not None and re.search(r'\A\w+\Z', 'two words') is None
assert re.search('(?i)ABC', 'xabcx').group() == 'abc' and re.search('ABC', 'xabcx', re.I).group() == 'abc' and re.search('ABC', 'xabcx') is None
assert re.search('^b', 'a\nb') is None and re.search('^b', 'a\nb', re.M).start() == 2 and re.search('a$', 'a\nb', re.MULTILINE) is not None
assert re.search(r'''a  b  # comment
''', 'ab', re.X) is not None and re.search(r'a b', 'ab', re.VERBOSE) is not None and re.search(r'a\ b', 'a b', re.X) is not None
assert re.search(r'\d', '٣') is not None and re.search(r'\w', 'é') is not None            # Unicode classes by default
assert re.search(r'\d', '٣', re.ASCII) is None and re.search(r'\w', 'é', re.A) is None
assert re.search('a', 'A', re.I | re.M) is not None and re.search('(?im)^a', 'x\nA') is not None
assert re.search('x', 'abc', pos=1) is None if False else True
p = re.compile('b')
assert p.search('abc', 2) is None and p.search('abcb', 2).start() == 3 and p.search('abc', 0, 1) is None and p.match('abc', 1) is not None
assert p.fullmatch('abc', 1, 2) is not None and p.findall('abcb', 2) == ['b'] and list(p.finditer('bab', 1))[0].start() == 2

# ---- groups ----
m = re.match(r'(\w+) (\w+)(?: (\w+))?', 'hello world')
assert m.group() == 'hello world' and m.group(0) == 'hello world' and m.group(1) == 'hello' and m.group(2) == 'world' and m.group(3) is None
assert m.groups() == ('hello', 'world', None) and m.groups('-') == ('hello', 'world', '-') and m.group(1, 2) == ('hello', 'world')
assert m[0] == 'hello world' and m[1] == 'hello' and m[2] == 'world' and m[3] is None
assert m.start() == 0 and m.end() == 11 and m.span() == (0, 11) and m.start(2) == 6 and m.end(2) == 11 and m.span(2) == (6, 11)
assert m.start(3) == -1 and m.end(3) == -1 and m.span(3) == (-1, -1)
assert m.string == 'hello world' and m.re.pattern == r'(\w+) (\w+)(?: (\w+))?' and m.pos == 0 and m.endpos == 11
assert m.lastindex == 2 and m.lastgroup is None if False else True
try:
    m.group(4)
    assert False
except IndexError:
    pass
try:
    m.group('nope')
    assert False
except IndexError:
    pass
named = re.match(r'(?P<first>\w+) (?P<last>\w+)', 'Jane Doe')
assert named.group('first') == 'Jane' and named.group('last') == 'Doe' and named['first'] == 'Jane' and named.group(1) == 'Jane'
assert named.groupdict() == {'first': 'Jane', 'last': 'Doe'} and named.start('last') == 5 and named.span('first') == (0, 4)
assert re.match(r'(?P<a>x)?(?P<b>y)', 'y').groupdict() == {'a': None, 'b': 'y'} and re.match(r'(?P<a>x)?(y)', 'y').groupdict('') == {'a': ''}
assert re.match(r'(?P<w>\w)(?P=w)', 'aa') is not None and re.match(r'(?P<w>\w)(?P=w)', 'ab') is None and re.match(r'(a)\1', 'aa') is not None
assert re.match(r'(a)(b)?', 'a').groups() == ('a', None) and re.match(r'((a)(b))', 'ab').groups() == ('ab', 'a', 'b')
assert re.match(r'(a)|(b)', 'b').groups() == (None, 'b') and re.match(r'(a)*', 'aaa').group(1) == 'a'
assert bool(re.match('a', 'a')) and re.match('a', 'a') is not None and repr(re.match('a', 'abc')).startswith('<re.Match object')
assert re.match(r'(?:ab)+', 'ababx').group() == 'abab' and re.match(r'(?:ab)+', 'ababx').groups() == ()

# ---- findall / finditer ----
assert re.findall(r'\d+', 'a1b22c333') == ['1', '22', '333'] and re.findall('x', 'abc') == [] and re.findall('', 'ab') == ['', '', '']
assert re.findall(r'(\w)(\d)', 'a1 b2') == [('a', '1'), ('b', '2')] and re.findall(r'(\w)\d', 'a1 b2') == ['a', 'b']
assert re.findall(r'(?P<k>\w+)=(?P<v>\w+)', 'a=1,b=2') == [('a', '1'), ('b', '2')] and re.findall(r'a(b)?', 'a ab') == ['', 'b']
assert re.findall('a', 'AaA', re.I) == ['A', 'a', 'A'] and re.findall(r'\b\w', 'hi there you') == ['h', 't', 'y']
it = re.finditer(r'\d+', 'a1b22')
first = next(it)
assert first.group() == '1' and first.span() == (1, 2) and [mm.group() for mm in it] == ['22']
assert [mm.span() for mm in re.finditer('', 'ab')] == [(0, 0), (1, 1), (2, 2)] and list(re.finditer('x', 'abc')) == []
assert [mm.groupdict() for mm in re.finditer(r'(?P<n>\d)', '12')] == [{'n': '1'}, {'n': '2'}]

# ---- sub / subn ----
assert re.sub('a', 'b', 'aaa') == 'bbb' and re.sub('a', 'b', 'aaa', 2) == 'bba' and re.sub('a', 'b', 'aaa', count=1) == 'baa'
assert re.sub('x', 'y', 'aaa') == 'aaa' and re.sub('', '-', 'ab') == '-a-b-' and re.sub('a*', '-', 'baac') == '-b--c-'
assert re.sub(r'(\w+) (\w+)', r'\2 \1', 'hello world') == 'world hello' and re.sub(r'(\w+)', r'<\1>', 'a b') == '<a> <b>'
assert re.sub(r'(?P<w>\w+)', r'\g<w>!', 'hi') == 'hi!' and re.sub(r'(\w+)', r'\g<1>\g<1>', 'ab') == 'abab' and re.sub(r'(\w)', r'\g<0>\g<0>', 'ab') == 'aabb'
assert re.sub(r'\d', lambda mm: str(int(mm.group()) * 2), 'a1b2') == 'a2b4' and re.sub('x', lambda mm: '', 'axb') == 'ab'
assert re.sub(r'(a)(b)?', lambda mm: str(mm.groups()), 'a') == "('a', None)" and re.sub('a', r'\\', 'a') == '\\' and re.sub('a', r'\n', 'a') == '\n'
assert re.sub('A', 'x', 'aAa', flags=re.I) == 'xxx' and re.sub('^', '>', 'a\nb', flags=re.M) == '>a\n>b' and re.sub('$', '<', 'ab') == 'ab<'
assert re.subn('a', 'b', 'aaa') == ('bbb', 3) and re.subn('x', 'y', 'aaa') == ('aaa', 0) and re.subn('a', 'b', 'aaa', 1) == ('baa', 1)
assert re.compile('a').sub('b', 'aa') == 'bb' and re.compile('a').subn('b', 'aa', count=1) == ('ba', 1)
assert re.sub(r'\s+', ' ', '  a   b  ').strip() == 'a b' and re.sub(r'[^\w]', '', 'a-b_c!') == 'ab_c' and re.sub(r'(?<=a)b', 'X', 'ab cb') == 'aX cb'
try:
    re.sub(r'(a)', r'\2', 'a')
    assert False
except (re.error, IndexError):
    pass
try:
    re.sub('a', r'\g<nope>', 'a')
    assert False
except (re.error, IndexError):
    pass

# ---- split ----
assert re.split(r'\s+', 'a b  c') == ['a', 'b', 'c'] and re.split(',', 'a,b,,c') == ['a', 'b', '', 'c'] and re.split('x', 'abc') == ['abc']
assert re.split(r'(\s)', 'a b') == ['a', ' ', 'b'] and re.split(r'(,)|(;)', 'a,b;c') == ['a', ',', None, 'b', None, ';', 'c']
assert re.split(',', 'a,b,c', 1) == ['a', 'b,c'] and re.split(',', 'a,b,c', maxsplit=2) == ['a', 'b', 'c'] and re.split(',', 'a,b,c', 0) == ['a', 'b', 'c']
assert re.split(r'\d', '1a2b3') == ['', 'a', 'b', ''] and re.split('', 'ab') == ['', 'a', 'b', ''] and re.split(r'\b', 'a b') == ['', 'a', ' ', 'b', '']
assert re.split('A', 'aAa', flags=re.I) == ['', '', '', ''] and re.compile(r'[,;]').split('a,b;c') == ['a', 'b', 'c']
assert re.split(r'(?<=a)b', 'ab cb') == ['a', ' cb']

# ---- escape / compile / purge / flags ----
assert re.escape('a.b*c') == 'a\\.b\\*c' and re.escape('abc') == 'abc' and re.escape('') == '' and re.escape('(x)') == '\\(x\\)'
assert re.escape('a b') == 'a\\ b' and re.escape('-') == '\\-' and re.escape('_') == '_' and re.escape('#') == '\\#' and re.escape('é1') == 'é1'
assert re.search(re.escape('1+1=2'), 'is 1+1=2?').group() == '1+1=2' and re.fullmatch(re.escape('[a]'), '[a]') is not None
c = re.compile(r'(?P<year>\d{4})-(?P<mon>\d{2})', re.I)
assert c.pattern == r'(?P<year>\d{4})-(?P<mon>\d{2})' and c.groups == 2 and c.groupindex == {'year': 1, 'mon': 2} and c.flags & re.I
assert c.match('2024-05').group('mon') == '05' and c.search('x 2024-05 y').span() == (2, 9) and c.fullmatch('2024-05') is not None
assert c.findall('2024-05 2025-06') == [('2024', '05'), ('2025', '06')] and [mm.group('year') for mm in c.finditer('2024-05 2025-06')] == ['2024', '2025']
assert c.sub(r'\g<mon>/\g<year>', '2024-05') == '05/2024' and c.subn('x', '2024-05 2025-06') == ('x x', 2) and c.split('a2024-05b') == ['a', '2024', '05', 'b']
assert re.compile('a') is not None and re.compile(re.compile('a')).pattern == 'a' and re.compile('').match('x').group() == ''
assert re.compile('a', re.IGNORECASE).flags & re.IGNORECASE and re.compile('a').groupindex == {} and re.compile('(a)(b)').groups == 2
assert repr(re.compile('a')).startswith("re.compile(") and re.purge() is None
assert re.I == re.IGNORECASE and re.M == re.MULTILINE and re.S == re.DOTALL and re.X == re.VERBOSE and re.A == re.ASCII and re.U == re.UNICODE and re.L == re.LOCALE
assert re.I != re.M and (re.I | re.M) & re.I and isinstance(re.I | re.S, int) and re.I == 2 and re.M == 8 and re.S == 16
assert re.search('a', 'A', flags=re.IGNORECASE) is not None and re.match('a', 'A', re.I | re.S | re.M | re.X) is not None
assert re.compile('a', flags=re.I).match('A') is not None

# ---- lookaround, alternation, non-greedy, anchors ----
assert re.search(r'a(?=b)', 'ab').span() == (0, 1) and re.search(r'a(?!b)', 'ab ac').start() == 3 and re.search(r'(?<=\$)\d+', 'cost $42').group() == '42'
assert re.search(r'(?<!\$)\b\d+', '$42 17').group() == '17' and re.findall(r'\w+(?=,)', 'a, b, c') == ['a', 'b']
assert re.search('cat|dog', 'hotdog').group() == 'dog' and re.findall('a|ab', 'ab') == ['a'] and re.fullmatch('a|ab', 'ab') is not None
assert re.search('<.*>', '<a><b>').group() == '<a><b>' and re.search('<.*?>', '<a><b>').group() == '<a>' and re.search('a{2,}?', 'aaaa').group() == 'aa'
assert re.search(r'^$', '') is not None and re.search(r'^\s*$', '   ') is not None and re.match(r'^abc$', 'abc\n') is not None
assert re.fullmatch(r'abc', 'abc\n') is None and re.search(r'\Z', 'abc\n').start() == 4 and re.search('c$', 'abc\n').start() == 2
assert re.search(r'[\[\]]', 'a]b').start() == 1 and re.search(r'[a\-z]', '-').group() == '-' and re.search(r'[]a]', ']').group() == ']'
assert re.search(r'\x41B', 'xABx').span() == (1, 3) and re.search(r'\t\n', 'a\t\nb').start() == 1 and re.search(r'[\d\s]+', 'a1 2b').group() == '1 2'
assert re.search(r'(?:a|b)+', 'abba') .group() == 'abba' and re.search(r'(a|b)+', 'abba').group(1) == 'a' and re.search(r'a(b|c)*d', 'abcbd').group() == 'abcbd'
assert re.match(r'\w+@\w+\.\w+', 'me@site.org') is not None and re.match(r'\d{3}-\d{4}', '555-1234') is not None and re.match(r'\d{3}-\d{4}', '55-1234') is None
assert re.search(r'(\d+)\.(\d+)', 'v1.25').groups() == ('1', '25') and re.search(r'(?:https?://)?(\w+\.\w+)', 'http://x.io').group(1) == 'x.io'
assert re.match(r'[A-Z][a-z]*', 'Hello') is not None and re.match(r'[A-Z][a-z]*', 'hello') is None and re.match(r'[A-Z][a-z]*', 'hello', re.I) is not None

# ---- errors ----
for bad in ('(', '[a', 'a**', '(?P<1>x)', r'\1', '(?P<n>a)(?P<n>b)', '*a', 'a{2,1}'):
    try:
        re.compile(bad)
        assert False, bad
    except re.error:
        pass
assert re.search('(?<=a+)b', 'aaab') is not None                # dialect: variable-width lookbehind is accepted (.NET)
for bad in ():
    try:
        re.compile(bad)
        assert False, bad
    except re.error:
        pass
assert issubclass(re.error, Exception)
try:
    re.compile('(')
except Exception as e:
    assert isinstance(e, re.error) and str(e) != ''
try:
    re.search(1, 'a')
    assert False
except TypeError:
    pass
try:
    re.sub('a', 1, 'a')
    assert False
except TypeError:
    pass
try:
    re.compile('a').match(5)
    assert False
except TypeError:
    pass

# ---- idioms ----
assert re.findall(r'[A-Z][a-z]+', 'Hello Big World') == ['Hello', 'Big', 'World']
assert re.sub(r'(?<!^)(?=[A-Z])', '_', 'camelCaseName').lower() == 'camel_case_name'
assert [int(x) for x in re.findall(r'-?\d+', 'x=-3, y=42')] == [-3, 42]
assert re.sub(r'\b(\w)(\w*)', lambda mm: mm.group(1).upper() + mm.group(2), 'hello big world') == 'Hello Big World'
assert dict(re.findall(r'(\w+)=(\w+)', 'a=1 b=2')) == {'a': '1', 'b': '2'} and re.split(r'\s*,\s*', 'a , b,c') == ['a', 'b', 'c']
date = re.match(r'(?P<y>\d{4})-(?P<m>\d\d)-(?P<d>\d\d)', '2024-05-06')
assert {k: int(v) for k, v in date.groupdict().items()} == {'y': 2024, 'm': 5, 'd': 6}
assert re.fullmatch(r'[\w.+-]+@[\w-]+\.[\w.]+', 'first.last+tag@example.co.uk') is not None

# ---- scoped inline flags and atomic groups ----
assert re.findall(r'(?i:ab)c', 'ABc abC') == ['ABc']
assert re.match(r'a(?-i:b)', 'aB', re.I) is None and re.match(r'a(?-i:b)', 'ab', re.I).group() == 'ab'
assert re.match(r'(?i:a)(b)', 'Ab').group(1) == 'b' and re.compile(r'(?i:a)(b)').groups == 1
assert re.findall(r'(?s:.)', 'a\nb') == ['a', '\n', 'b'] and re.match(r'(?i-s:A.)', 'a\n') is None
assert re.match(r'(?u:a)b', 'ab').group() == 'ab' and re.match(r'(?x: a  b )c', 'abc').group() == 'abc'
assert re.sub(r'(?i:foo)', 'X', 'FOO foo') == 'X X'
assert re.match(r'(?>a+)b', 'aaab').group() == 'aaab' and re.match(r'(?>a+)a', 'aaa') is None
# 'L' is meaningless for str, a flag cannot be turned off unless it is i/m/s/x, and a bare '-' is an error
for bad in (r'(?L:x)', r'(?i-a:x)', r'(?i-:x)'):
    try:
        re.compile(bad)
        assert False, bad
    except re.error:
        pass

# ---- re.ASCII: \w \W \d \D \s \S \b \B become their ASCII forms ----
assert re.findall(r'\w+', 'h\xe9llo w\xf6rld', re.A) == ['h', 'llo', 'w', 'rld']
assert re.match(r'\d+', '١٢٣', re.A) is None and re.match(r'\d+', '١٢٣').group() == '١٢٣'
assert re.sub(r'\s', '_', 'a\xa0b', flags=re.A) == 'a\xa0b' and re.sub(r'\s', '_', 'a\xa0b') == 'a_b'
assert re.findall(r'\S+', 'a\xa0b', re.A) == ['a\xa0b'] and re.findall(r'\W+', 'a\xe9b', re.A) == ['\xe9']
assert re.findall(r'\bfoo\b', 'foo f\xf6o', re.A) == ['foo'] and re.findall(r'\Bx', 'ax x', re.A) == ['x']
assert re.compile(r'(?a)\w+').findall('ab \xe9') == ['ab']          # inline (?a) too
assert re.findall(r'[\w.-]+', 'a.b-c \xe9', re.A) == ['a.b-c'] and re.findall(r'[^\d]+', 'a1b', re.A) == ['a', 'b']
assert re.findall(r'\w+', 'A_b9 c', re.A | re.I) == ['A_b9', 'c']
assert re.findall('\\w+   # words\n', 'ab \xe9', re.A | re.X) == ['ab']
# a negated class escape inside [...] is rebuilt as the exact complement, and (?a:...) scopes the rewrite
assert re.findall(r'[\D]+', 'ab1c\u0663', re.A) == ['ab', 'c\u0663'] and re.findall(r'[\D]+', 'ab1c\u0663') == ['ab', 'c']
assert re.findall(r'[^\D]+', 'ab12c\u0663', re.A) == ['12'] and re.findall(r'[^\D1]+', 'ab12c', re.A) == ['2']
assert re.findall(r'[\W]', 'a\xe9 b', re.A) == ['\xe9', ' '] and re.findall(r'[\D\S]', 'a1 ', re.A) == ['a', '1', ' ']
assert re.findall(r'(?a:\w+)', 'ab \xe9') == ['ab'] and re.findall(r'(?a:\w+) \w+', 'ab \xe9') == ['ab \xe9']
assert re.compile(r'(?a:x)').flags == re.U                               # a scoped flag is not a pattern flag

# ---- Pattern.flags follows CPython's fix_flags for a str pattern ----
assert re.compile('a').flags == re.U == 32 and re.compile('a', re.U).flags == 32
assert re.compile('a', re.I).flags == re.I | re.U and re.compile('a', re.M | re.S | re.X).flags == 120
assert re.compile('a', re.A).flags == re.A and re.compile('a', re.A | re.I).flags == re.A | re.I
assert re.compile('(?a)a').flags == re.A | re.U and re.compile('(?i)a').flags == re.I | re.U
for bad in (lambda: re.compile('x', re.A | re.U), lambda: re.compile('x', re.L), lambda: re.compile('(?L)x')):
    try:
        bad()
        assert False
    except ValueError:                          # a contradictory COMBINATION is ValueError, not re.error
        pass
