# Adapted from CPython's test suite (Lib/test/string_tests.py: BaseTest.test_find/test_rfind/test_index/
# test_count/test_split/test_rsplit/test_strip/test_ljust/test_rjust/test_center/test_zfill/test_swapcase/
# test_title/test_capitalize/test_expandtabs/test_partition/test_join/test_replace/test_translate,
# Lib/test/test_unicode.py isidentifier/isprintable/casefold/removeprefix).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every str method the dialect exposes is exercised here.

# ---- search: find / rfind / index / rindex / count / startswith / endswith ----
s = 'abcabcabc'
assert s.find('bc') == 1 and s.find('bc', 2) == 4 and s.find('bc', 2, 5) == -1 and s.find('x') == -1
assert s.find('') == 0 and s.find('', 9) == 9 and s.find('', 10) == -1 and s.find('a', -3) == 6
assert s.rfind('bc') == 7 and s.rfind('bc', 0, 8) == 4 and s.rfind('x') == -1 and s.rfind('') == 9
assert s.index('c') == 2 and s.rindex('c') == 8 and s.index('c', 3) == 5
for bad in (lambda: s.index('x'), lambda: s.rindex('x'), lambda: s.index('a', 7)):
    try:
        bad()
        assert False
    except ValueError:
        pass
assert s.count('abc') == 3 and s.count('a', 1) == 2 and s.count('a', 1, 4) == 1 and s.count('') == 10
assert 'aaaa'.count('aa') == 2                              # non-overlapping
assert s.startswith('abc') and s.startswith(('x', 'ab')) and not s.startswith('bc')
assert s.startswith('bc', 1) and not s.startswith('', 100) and not s.startswith('a', 1, 1)   # start past the end: False
assert s.endswith('bc') and s.endswith(('x', 'c')) and s.endswith('ab', 0, 2) and not s.endswith('ab')
try:
    s.startswith(1)
    assert False
except TypeError:
    pass
try:
    s.find(1)
    assert False
except TypeError:
    pass

# ---- replace / removeprefix / removesuffix ----
assert 'aaa'.replace('a', 'b') == 'bbb' and 'aaa'.replace('a', 'b', 2) == 'bba' and 'aaa'.replace('a', 'b', 0) == 'aaa'
assert 'abc'.replace('', '-') == '-a-b-c-' and 'abc'.replace('x', 'y') == 'abc' and ''.replace('', 'x') == 'x'
assert 'a.b.c'.replace('.', '') == 'abc' and 'abc'.replace('b', 'XYZ') == 'aXYZc'
assert 'aaa'.replace('a', 'b', -1) == 'bbb'
assert 'TestHook'.removeprefix('Test') == 'Hook' and 'BaseTestCase'.removeprefix('Test') == 'BaseTestCase'
assert 'MiscTests'.removesuffix('Tests') == 'Misc' and 'TmpDirMixin'.removesuffix('Tests') == 'TmpDirMixin'
assert 'abc'.removeprefix('') == 'abc' and 'abc'.removesuffix('abc') == '' and 'abc'.removeprefix('abcd') == 'abc'

# ---- split / rsplit / splitlines / partition / rpartition ----
assert 'a b  c'.split() == ['a', 'b', 'c'] and '  a b '.split() == ['a', 'b'] and ''.split() == [] and '   '.split() == []
assert 'a,b,,c'.split(',') == ['a', 'b', '', 'c'] and ',a,'.split(',') == ['', 'a', ''] and ''.split(',') == ['']
assert 'a b c d'.split(None, 2) == ['a', 'b', 'c d'] and 'a,b,c'.split(',', 1) == ['a', 'b,c']
assert 'a,b,c'.split(',', 0) == ['a,b,c'] and 'a,b,c'.split(',', -1) == ['a', 'b', 'c']
assert 'a<>b<>c'.split('<>') == ['a', 'b', 'c'] and 'a\tb\nc\rd'.split() == ['a', 'b', 'c', 'd']
assert 'a b c d'.rsplit(None, 2) == ['a b', 'c', 'd'] and 'a,b,c'.rsplit(',', 1) == ['a,b', 'c']
assert 'a,b,c'.rsplit(',') == ['a', 'b', 'c'] and '  a b '.rsplit() == ['a', 'b'] and 'a,b'.rsplit(maxsplit=1) == ['a,b']
assert 'a b c'.split(maxsplit=1) == ['a', 'b c'] and 'a b c'.split(sep=' ') == ['a', 'b', 'c']
try:
    'abc'.split('')
    assert False
except ValueError:
    pass
assert 'a\nb\r\nc\rd'.splitlines() == ['a', 'b', 'c', 'd'] and 'a\nb\n'.splitlines() == ['a', 'b']
assert 'a\nb\n'.splitlines(True) == ['a\n', 'b\n'] and ''.splitlines() == [] and 'a'.splitlines() == ['a']
assert 'a\n\nb'.splitlines() == ['a', '', 'b'] and 'a\x0bb\x0cc\x1cd\x1de\x1ef\x85g h i'.splitlines() == list('abcdefghi')
assert 'a\nb'.splitlines(keepends=True) == ['a\n', 'b']
assert 'key=value=x'.partition('=') == ('key', '=', 'value=x') and 'novalue'.partition('=') == ('novalue', '', '')
assert 'key=value=x'.rpartition('=') == ('key=value', '=', 'x') and 'novalue'.rpartition('=') == ('', '', 'novalue')
assert ''.partition('=') == ('', '', '')
try:
    'abc'.partition('')
    assert False
except ValueError:
    pass

# ---- join ----
assert ','.join(['a', 'b', 'c']) == 'a,b,c' and ''.join(['a', 'b']) == 'ab' and '-'.join([]) == '' and '-'.join(['a']) == 'a'
assert ' '.join('abc') == 'a b c' and ', '.join(('x', 'y')) == 'x, y' and '\n'.join({'k': 1}) == 'k'
assert ''.join(str(i) for i in range(5)) == '01234' and '|'.join(map(str, [1, 2])) == '1|2'
try:
    ','.join([1, 2])
    assert False
except TypeError:
    pass
try:
    ','.join('a', 'b')
    assert False
except TypeError:
    pass

# ---- strip family ----
assert '  x  '.strip() == 'x' and '  x  '.lstrip() == 'x  ' and '  x  '.rstrip() == '  x'
assert 'xxhixx'.strip('x') == 'hi' and 'xxhixx'.lstrip('x') == 'hixx' and 'xxhixx'.rstrip('x') == 'xxhi'
assert 'abcba'.strip('ab') == 'c' and '\t\n x \r'.strip() == 'x' and ''.strip() == '' and 'xxx'.strip('x') == ''
assert 'www.example.com'.strip('cmowz.') == 'example' and 'abc'.strip(None) == 'abc'
assert ' x '.strip() == 'x'                        # unicode whitespace
try:
    'abc'.strip(1)
    assert False
except TypeError:
    pass

# ---- case ----
assert 'Hello World'.upper() == 'HELLO WORLD' and 'Hello World'.lower() == 'hello world'
assert 'Hello World'.swapcase() == 'hELLO wORLD' and 'hello world'.title() == 'Hello World'
assert 'hello world'.capitalize() == 'Hello world' and 'HELLO'.capitalize() == 'Hello' and ''.capitalize() == ''
assert "they're bill's".title() == "They'Re Bill'S" and 'x1y'.title() == 'X1Y'
assert 'É'.lower() == 'é' and 'é'.upper() == 'É' and 'Ω'.lower() == 'ω' and 'HeLLo'.casefold() == 'hello'
assert 'ß'.upper() == 'SS' and 'straße'.casefold() == 'strasse'   # full (length-changing) mapping
assert 'Straße'.casefold() == 'STRASSE'.casefold()

# ---- padding: ljust / rjust / center / zfill / expandtabs ----
assert 'ab'.ljust(5) == 'ab   ' and 'ab'.ljust(5, '*') == 'ab***' and 'ab'.ljust(1) == 'ab'
assert 'ab'.rjust(5) == '   ab' and 'ab'.rjust(5, '*') == '***ab'
assert 'ab'.center(6) == '  ab  ' and 'ab'.center(5) == '  ab ' and 'abc'.center(6, '*') == '*abc**' and 'ab'.center(1) == 'ab'
assert 'a'.center(4) == ' a  ' and 'a'.center(3) == ' a '
assert '42'.zfill(5) == '00042' and '-42'.zfill(5) == '-0042' and '+42'.zfill(5) == '+0042' and '42'.zfill(1) == '42'
assert ''.zfill(3) == '000' and 'abc'.zfill(5) == '00abc' and '-'.zfill(3) == '-00'
assert 'a\tb'.expandtabs() == 'a       b' and 'a\tb'.expandtabs(4) == 'a   b' and 'ab\tc'.expandtabs(4) == 'ab  c'
assert 'a\tb'.expandtabs(0) == 'ab' and 'a\nb\tc'.expandtabs(4) == 'a\nb   c' and '\t'.expandtabs(1) == ' '
assert 'a\tb'.expandtabs(tabsize=2) == 'a b'
try:
    'ab'.ljust(5, '**')
    assert False
except TypeError:
    pass
try:
    'ab'.center(5, '')
    assert False
except TypeError:
    pass

# ---- predicates ----
assert '123'.isdigit() and not '12.3'.isdigit() and not ''.isdigit() and '²'.isdigit() and not '²'.isdecimal()
assert '123'.isdecimal() and '١٢٣'.isdecimal() and '123'.isnumeric() and '½'.isnumeric() and not '½'.isdigit()
assert 'abc'.isalpha() and not 'ab1'.isalpha() and not ''.isalpha() and 'é'.isalpha() and not ' '.isalpha()
assert 'abc1'.isalnum() and not 'ab c'.isalnum() and not ''.isalnum()
assert ' \t\n'.isspace() and not ''.isspace() and not ' a'.isspace() and ' '.isspace()
assert 'abc'.islower() and 'abc1'.islower() and not 'aBc'.islower() and not '123'.islower() and not ''.islower()
assert 'ABC'.isupper() and 'AB1'.isupper() and not 'AbC'.isupper() and not '123'.isupper()
assert 'Hello World'.istitle() and not 'Hello world'.istitle() and not ''.istitle() and 'A'.istitle() and not 'a'.istitle()
assert 'abc'.isascii() and ''.isascii() and not 'é'.isascii() and '\x7f'.isascii()
assert 'abc 123'.isprintable() and ''.isprintable() and not 'a\nb'.isprintable() and not '\x00'.isprintable()
# dialect: the predicates classify a surrogate pair as one character, while len counts two units
assert '\U0001F600'.isprintable() and not '\U0001F600'.isalpha() and len('\U0001F600') == 2
assert '\U0001D400'.isalpha() and '\U0001D400'.isupper() and '\U0001D41A'.islower() and '\U0001D7CE'.isdigit()
assert not '\ud83d'.isprintable() and not '\ud83d'.isalpha()
assert 'abc'.isidentifier() and '_x1'.isidentifier() and not '1x'.isidentifier() and not ''.isidentifier()
assert not 'a-b'.isidentifier() and 'if'.isidentifier() and 'é'.isidentifier() and not ' a'.isidentifier()

# ---- format / format_map ----
assert '{} {}'.format(1, 2) == '1 2' and '{1} {0}'.format('a', 'b') == 'b a' and '{x}-{y}'.format(x=1, y=2) == '1-2'
assert '{0}{0}'.format('r') == 'rr' and '{:>4}'.format('a') == '   a' and '{!r}'.format('s') == "'s'"
assert '{{}}'.format() == '{}' and '{{{}}}'.format(1) == '{1}' and '{:.2f}'.format(2.345) == '2.35'
assert '{0:{1}}'.format(7, '03') == '007' and '{:{}}'.format('x', 3) == 'x  '
assert '{a}'.format_map({'a': 1}) == '1' and '{a} {b}'.format_map({'a': 1, 'b': 2}) == '1 2'
try:
    '{}'.format()
    assert False
except IndexError:
    pass
try:
    '{x}'.format()
    assert False
except KeyError:
    pass
try:
    '{'.format()
    assert False
except ValueError:
    pass
try:
    '{a}'.format_map({})
    assert False
except KeyError:
    pass

# ---- translate / maketrans ----
table = str.maketrans('abc', 'xyz')
assert 'aabbcc'.translate(table) == 'xxyyzz' and 'def'.translate(table) == 'def'
assert 'abc'.translate(str.maketrans('a', 'b', 'c')) == 'bb'
assert 'abc'.translate(str.maketrans({'a': '1', 'b': None})) == '1c'
assert 'abc'.translate({ord('a'): 'A'}) == 'Abc' and 'abc'.translate({ord('a'): None}) == 'bc'
assert 'abc'.translate({ord('a'): 65}) == 'Abc' and 'abc'.translate({}) == 'abc'
assert str.maketrans('ab', 'AB') == {97: 65, 98: 66}
try:
    str.maketrans('ab', 'A')
    assert False
except ValueError:
    pass

# ---- encode ----
assert 'abc'.encode() == b'abc' and 'é'.encode() == b'\xc3\xa9' and 'é'.encode('utf-8') == b'\xc3\xa9'
assert 'é'.encode('latin-1') == b'\xe9' and 'abc'.encode('ascii') == b'abc' and 'a'.encode('UTF8') == b'a'
assert '☺'.encode('utf-8') == b'\xe2\x98\xba' and 'ÿ'.encode('iso-8859-1') == b'\xff'
try:
    'é'.encode('ascii')
    assert False
except (UnicodeEncodeError if False else ValueError):
    pass
try:
    'a'.encode('no-such-codec')
    assert False
except LookupError:
    pass

# ---- str is immutable and sequence-like ----
t = 'hello'
assert t[0] == 'h' and t[-1] == 'o' and t[1:3] == 'el' and len(t) == 5 and list(t) == ['h', 'e', 'l', 'l', 'o']
assert 'l' in t and 'll' in t and 'z' not in t and t * 2 == 'hellohello' and t + '!' == 'hello!'
assert min(t) == 'e' and max(t) == 'o' and sorted(t) == ['e', 'h', 'l', 'l', 'o'] and t.count('l') == 2
try:
    t[0] = 'j'
    assert False
except TypeError:
    pass
try:
    t[10]
    assert False
except IndexError:
    pass
assert 'abc' == 'abc' and 'abc' != 'abd' and 'abc' < 'abd' and 'ab' < 'abc' and 'B' < 'a' and '' < 'a'
assert hash('abc') == hash('ab' + 'c')
assert str(t) is t                                              # str() of a str is the same object

# ---- unbound methods via the type ----
assert sorted(['b', 'A', 'c'], key=str.lower) == ['A', 'b', 'c']
assert list(map(str.strip, [' a ', 'b '])) == ['a', 'b']
assert list(filter(str.isdigit, ['1', 'x', '22'])) == ['1', '22']
assert str.upper('abc') == 'ABC' and str.join('-', ['a', 'b']) == 'a-b' and str.replace('aaa', 'a', 'b', 1) == 'baa'
try:
    str.upper(1)
    assert False
except TypeError:
    pass
try:
    str.upper()
    assert False
except TypeError:
    pass

# ---- isidentifier is PEP 3131 exactly (XID_Start / XID_Continue), not a category approximation ----
assert 'abc'.isidentifier() and '_x'.isidentifier() and '_'.isidentifier() and 'a1'.isidentifier()
assert not '1a'.isidentifier() and not ''.isidentifier() and not 'a b'.isidentifier()
assert 'переменная'.isidentifier() and '变量'.isidentifier()
assert 'a·'.isidentifier() and 'a·'.isidentifier()      # Other_ID_Continue
assert '℘'.isidentifier() and '℮'.isidentifier()        # Other_ID_Start
assert not 'a²'.isidentifier() and not 'Ⓐ'.isidentifier() and not '😀'.isidentifier()
assert 'Ⅰ'.isidentifier()                               # roman numeral one (Nl)
assert not '́a'.isidentifier()                     # a combining mark cannot start one
assert '\U0001d54f'.isidentifier() and 'a\U0001d54f'.isidentifier()   # walks code points

# ---- format_map looks each name up in the mapping as it is met, so __missing__ answers ----
from collections import defaultdict, ChainMap, Counter
assert '{a}{b}'.format_map(defaultdict(lambda: 'x')) == 'xx'
assert '{a}{b}'.format_map(ChainMap({'a': 1}, {'b': 2})) == '12'
assert '{a}'.format_map(Counter('aab')) == '2'
try:
    '{a}'.format_map(3)
    assert False
except TypeError:
    pass

# ---- translate / maketrans count code points, so an astral character is ONE key ----
smile = chr(0x1F600)
assert smile.translate({0x1F600: 'X'}) == 'X' and ('a' + smile + 'b').translate({0x1F600: None}) == 'ab'
assert (smile + 'x').translate(str.maketrans(smile, 'y')) == 'yx'
assert 'ab'.translate(str.maketrans('ab', smile + chr(0x1F601))) == smile + chr(0x1F601)
assert (smile + 'x').translate(str.maketrans('', '', smile)) == 'x'
try:
    str.maketrans('a', smile + smile)
    assert False
except ValueError:
    pass
from collections import defaultdict
assert 'ab'.translate(defaultdict(lambda: 'z')) == 'zz'            # any subscriptable table
assert 'abc'.translate(['X']) == 'abc'                             # an IndexError keeps the character
