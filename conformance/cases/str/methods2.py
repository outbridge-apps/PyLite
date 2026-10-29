# Adapted from CPython's test suite (Lib/test/test_str.py, string_tests.py mixin cases).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# index/rindex raise where find/rfind return -1
assert 'abcabc'.index('b') == 1
assert 'abcabc'.rindex('b') == 4
raised = False
try:
    'abc'.index('x')
except ValueError:
    raised = True
assert raised

# find/index with start and end
assert 'abcabc'.find('b', 2) == 4
assert 'abcabc'.find('b', 2, 4) == -1
assert 'abcabc'.count('b', 2) == 1

# translate with a maketrans table
table = str.maketrans('ab', '12')
assert 'abcab'.translate(table) == '12c12'
drop = str.maketrans('', '', 'aeiou')
assert 'sequoia'.translate(drop) == 'sq'

# format_map
assert '{x}-{y}'.format_map({'x': 1, 'y': 2}) == '1-2'

# predicates on edge inputs
assert not ''.isdigit()
assert not ''.isalpha()
assert 'Hello World'.istitle()
assert not 'Hello world'.istitle()

# str comparison follows code points, not locale
assert 'a' < 'b'
assert 'abc' < 'abd'
assert 'ab' < 'abc'
assert max(['apple', 'Zoo']) == 'apple'      # 'Z' < 'a'

# repetition edge cases
assert 'ab' * 0 == ''
assert 'ab' * -1 == ''

# strip with an empty result
assert 'aaa'.strip('a') == ''
assert ''.strip() == ''

# split limit of 0
assert 'a b c'.split(' ', 0) == ['a b c']

# in / not in as substring test
assert 'bc' in 'abcd'
assert '' in 'abc'
assert 'abc' in 'abc'

# multiplication combined with join for building rows
assert '|'.join(['-' * 3] * 2) == '---|---'

# startswith/endswith with start/end positions
assert 'abcdef'.startswith('cd', 2)
assert 'abcdef'.endswith('cd', 0, 4)
