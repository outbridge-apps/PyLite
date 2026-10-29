# Adapted from CPython's test suite (Lib/test/test_str.py, string_tests.py mixin cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# split: the None separator collapses runs; an explicit one does not
assert 'a  b c'.split() == ['a', 'b', 'c']
assert 'a  b'.split(' ') == ['a', '', 'b']
assert ''.split() == []
assert ''.split(',') == ['']
assert ',a,,'.split(',') == ['', 'a', '', '']
assert 'a b c'.split(' ', 1) == ['a', 'b c']
assert 'a b c'.rsplit(' ', 1) == ['a b', 'c']
assert 'ab\r\ncd\ne'.splitlines() == ['ab', 'cd', 'e']

# strip family
assert '  x  '.strip() == 'x'
assert '  x  '.lstrip() == 'x  '
assert '  x  '.rstrip() == '  x'
assert 'xxhixx'.strip('x') == 'hi'
assert 'www.example.com'.strip('cmowz.') == 'example'

# replace
assert 'one one one'.replace('one', 'two') == 'two two two'
assert 'hello'.replace('l', 'L', 1) == 'heLlo'
assert 'abc'.replace('', '-') == '-a-b-c-'

# search
assert 'abcabc'.find('b') == 1
assert 'abcabc'.rfind('b') == 4
assert 'abc'.find('x') == -1
assert 'abcabc'.count('bc') == 2
assert 'Hello'.startswith('He')
assert 'Hello'.startswith(('Wo', 'He'))
assert 'Hello'.endswith(('lo', 'xx'))

# case transforms
assert 'MiXeD'.lower() == 'mixed'
assert 'MiXeD'.upper() == 'MIXED'
assert 'hello world'.title() == 'Hello World'
assert "they're".title() == "They'Re"     # the classic title() quirk
assert 'hello'.capitalize() == 'Hello'
assert 'AbC'.swapcase() == 'aBc'

# predicates
assert '123'.isdigit() and not '12a'.isdigit()
assert 'abc'.isalpha() and not 'a b'.isalpha()
assert 'a1'.isalnum() and not 'a 1'.isalnum()
assert '  \t\n'.isspace() and not ''.isspace()
assert 'HELLO'.isupper() and 'hello'.islower()

# padding
assert 'ab'.ljust(5) == 'ab   '
assert 'ab'.rjust(5) == '   ab'
assert 'x'.center(5, '*') == '**x**'
assert '42'.zfill(5) == '00042'
assert '-42'.zfill(5) == '-0042'
assert '42'.zfill(1) == '42'

# join
assert ','.join(['a', 'b', 'c']) == 'a,b,c'
assert ','.join(['a']) == 'a'
assert ''.join([]) == ''
assert '-'.join('abc') == 'a-b-c'

# partition, removeprefix/removesuffix
assert 'key=value=x'.partition('=') == ('key', '=', 'value=x')
assert 'key=value=x'.rpartition('=') == ('key=value', '=', 'x')
assert 'abc'.partition('|') == ('abc', '', '')
assert 'TestCase'.removeprefix('Test') == 'Case'
assert 'TestCase'.removeprefix('X') == 'TestCase'
assert 'file.txt'.removesuffix('.txt') == 'file'

# slicing and indexing
s = 'abcdef'
assert s[0] == 'a' and s[-1] == 'f'
assert s[1:4] == 'bcd'
assert s[2:100] == 'cdef'
assert s[::2] == 'ace'
assert s[::-1] == 'fedcba'
assert s[4:1:-1] == 'edc'

# concatenation, repetition, comparison
assert 'ab' + 'cd' == 'abcd'
assert 'ab' * 3 == 'ababab'
assert 'a' < 'b' < 'ba'
assert 'Z' < 'a'                     # ordinal comparison
assert 'abc' == 'ab' + 'c'

# chr/ord round trip
assert ord('A') == 65 and chr(65) == 'A'
assert chr(0x44F) == 'я'
assert len('я') == 1
