# Adapted from CPython's test suite (Lib/test/test_bytes.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# literals and construction
b = b'abc'
assert len(b) == 3
assert b[0] == 97                       # indexing a bytes yields ints
assert b[-1] == 99
assert b[1:] == b'bc'
assert bytes([65, 66, 67]) == b'ABC'
assert b'' == bytes()
assert b'ab' + b'cd' == b'abcd'
assert b'ab' * 2 == b'abab'
assert b'ab' != 'ab'                    # bytes never equal str

# encode/decode round trip
s = 'héllo'
data = s.encode('utf-8')
assert data.decode('utf-8') == s
assert 'abc'.encode() == b'abc'
assert b'abc'.decode() == 'abc'
assert len('я'.encode('utf-8')) == 2    # two UTF-8 bytes

# iteration yields ints
assert list(b'AB') == [65, 66]
assert 97 in b'abc'

# escapes inside bytes literals
assert b'\x41' == b'A'
assert len(b'\n') == 1
