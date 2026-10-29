# Adapted from CPython's test suite (Lib/test/test_string_literals.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# escape sequences
assert len('\n') == 1 and len('\t') == 1
assert '\x41' == 'A'
assert 'Ж' == 'Ж'
assert '\\' == chr(92)
assert '\'' == chr(39)
assert "\"" == chr(34)
assert '\0' == chr(0)
assert len('a\0b') == 3

# raw strings keep the backslash
assert r'\n' == '\\n'
assert len(r'\n') == 2
assert r'a\tb' == 'a' + chr(92) + 'tb'

# adjacent string literals concatenate at parse time
assert 'ab' 'cd' == 'abcd'
assert ('one '
        'two') == 'one two'

# quotes inside the other quote style
assert "it's" == 'it' + chr(39) + 's'
assert 'say "hi"' == 'say ' + chr(34) + 'hi' + chr(34)

# triple-quoted strings span lines
s = '''line1
line2'''
assert s == 'line1\nline2'
assert """a"b""" == 'a"b'

# f-string vs raw combination
x = 1
assert f'{x}\n'[-1] == chr(10)
assert rf'{x}\n' == '1\\n'

# unicode content round-trips through operations
word = 'привет'
assert len(word) == 6
assert word.upper() == 'ПРИВЕТ'
assert word[0] == 'п'
assert word[::-1] == 'тевирп'
