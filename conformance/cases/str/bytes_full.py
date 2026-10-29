# Adapted from CPython's test suite (Lib/test/test_bytes.py: BaseBytesTest.test_basics/test_from_iterable/
# test_hex/test_fromhex/test_decode/test_join/test_split/test_strip/test_find/test_replace/test_count,
# Lib/test/test_long.py to_bytes/from_bytes). Python Software Foundation, PSF License Version 2 -- see README.
# Every bytes method the dialect exposes is exercised here.

# ---- construction ----
assert bytes() == b'' and bytes(3) == b'\x00\x00\x00' and bytes([65, 66]) == b'AB' and bytes(b'x') == b'x'
assert bytes('é', 'utf-8') == b'\xc3\xa9' and bytes('abc', 'ascii') == b'abc' and bytes(range(3)) == b'\x00\x01\x02'
assert bytes((255,)) == b'\xff' and bytes([]) == b''
try:
    bytes([256])
    assert False
except ValueError:
    pass
try:
    bytes([-1])
    assert False
except ValueError:
    pass
try:
    bytes('abc')                     # str needs an encoding
    assert False
except TypeError:
    pass
try:
    bytes(-1)
    assert False
except ValueError:
    pass
try:
    bytes(['a'])
    assert False
except TypeError:
    pass

# ---- literals ----
assert b'abc' == b'\x61\x62\x63' and b'\n' == b'\x0a' and b'\\' == b'\x5c' and b"'" == b'\x27' and rb'\n' == b'\\n'
assert b'a' b'b' == b'ab' and B'x' == b'x' and b'''tri''' == b'tri'
assert b'\x00\xff' == bytes([0, 255]) and b'\t\r\n' == bytes([9, 13, 10])

# ---- sequence behaviour ----
b = b'hello'
assert len(b) == 5 and b[0] == 104 and b[-1] == 111 and b[1:3] == b'el' and b[::-1] == b'olleh' and b[:] == b
assert isinstance(b[0], int) and isinstance(b[0:1], bytes) and b[0:1] == b'h'
assert list(b) == [104, 101, 108, 108, 111] and 104 in b and b'ell' in b and b'z' not in b and 200 not in b
assert b + b'!' == b'hello!' and b * 2 == b'hellohello' and 2 * b'ab' == b'abab' and b * 0 == b''
assert b == b'hello' and b != b'Hello' and b'a' < b'b' and b'ab' < b'b' and b'' < b'a' and b'\xff' > b'\x00'
assert max(b) == 111 and min(b) == 101 and sorted(b'cab') == [97, 98, 99] and sum(b'\x01\x02') == 3
assert hash(b'abc') == hash(b'ab' + b'c') and {b'k': 1}[b'k'] == 1 and b'a' in {b'a'}
assert bool(b'') is False and bool(b'\x00') is True
try:
    b[10]
    assert False
except IndexError:
    pass
try:
    b[0] = 1
    assert False
except TypeError:
    pass
try:
    b + 'str'
    assert False
except TypeError:
    pass
try:
    b'a' < 'a'
    assert False
except TypeError:
    pass
assert b'a' != 'a'                                       # never equal to str

# ---- repr / str ----
assert repr(b'abc') == "b'abc'" and repr(b'\x00\xff') == "b'\\x00\\xff'" and repr(b'\n') == "b'\\n'"
assert repr(b"'") == 'b"\'"' and repr(b'"') == "b'\"'" and repr(b'\t') == "b'\\t'" and repr(b'\\') == "b'\\\\'"
assert str(b'abc') == "b'abc'" and repr(b'') == "b''"

# ---- decode / hex / fromhex ----
assert b'abc'.decode() == 'abc' and b'\xc3\xa9'.decode('utf-8') == 'é' and b'\xe9'.decode('latin-1') == 'é'
assert b'abc'.decode('ascii') == 'abc' and b'a'.decode('UTF-8') == 'a' and b'a'.decode('latin1') == 'a'
assert b'\xff'.decode('utf-8', 'replace') == '�' and b'\xff'.decode('utf-8', 'ignore') == '' and b'a\xffb'.decode('utf-8', errors='ignore') == 'ab'
assert b'\xe9\x00'.decode('utf-16-le') == 'é'
# dialect: the codec set is utf-8 / utf-8-sig / ascii / latin-1 / cp1251 / cp1252 / cp866 / koi8-r / iso-8859-5 / utf-16[-le|-be]
try:
    b'a'.decode('utf-32')      # a four-byte codec with paths of its own; nothing an integration meets
    assert False
except LookupError:
    pass
try:
    b'\xff'.decode('utf-8')
    assert False
except ValueError:
    pass
try:
    b'\xe9'.decode('ascii')
    assert False
except ValueError:
    pass
try:
    b'a'.decode('nope')
    assert False
except LookupError:
    pass
assert b'\x01\xab'.hex() == '01ab' and b''.hex() == '' and b'\x01\xab'.hex(':') == '01:ab' and b'\x01\x02\x03'.hex('-', 2) == '01-0203'
assert bytes.fromhex('01ab') == b'\x01\xab' and bytes.fromhex('01 AB  ff') == b'\x01\xab\xff' and bytes.fromhex('') == b''
try:
    bytes.fromhex('0g')
    assert False
except ValueError:
    pass
try:
    bytes.fromhex('abc')
    assert False
except ValueError:
    pass

# ---- search ----
h = b'abcabc'
assert h.find(b'bc') == 1 and h.find(b'bc', 2) == 4 and h.find(b'x') == -1 and h.find(98) == 1 and h.find(b'') == 0
assert h.rfind(b'bc') == 4 and h.rfind(b'bc', 0, 4) == 1 and h.rfind(b'x') == -1
assert h.index(b'c') == 2 and h.rindex(b'c') == 5 and h.count(b'abc') == 2 and h.count(b'') == 7 and h.count(97) == 2
assert h.startswith(b'ab') and h.startswith((b'x', b'a')) and not h.startswith(b'b') and h.startswith(b'bc', 1)
assert h.endswith(b'bc') and h.endswith((b'x', b'c')) and h.endswith(b'ab', 0, 2)
try:
    h.index(b'x')
    assert False
except ValueError:
    pass
try:
    h.find('bc')
    assert False
except TypeError:
    pass

# ---- transform ----
assert b'aaa'.replace(b'a', b'b') == b'bbb' and b'aaa'.replace(b'a', b'b', 1) == b'baa' and b'abc'.replace(b'x', b'y') == b'abc'
assert b'a b  c'.split() == [b'a', b'b', b'c'] and b'a,b,,c'.split(b',') == [b'a', b'b', b'', b'c'] and b'a,b,c'.split(b',', 1) == [b'a', b'b,c']
assert b'a,b,c'.rsplit(b',', 1) == [b'a,b', b'c'] and b' a b '.rsplit() == [b'a', b'b'] and b''.split() == []
assert b','.join([b'a', b'b']) == b'a,b' and b''.join([]) == b'' and b'-'.join([b'x']) == b'x'
assert b'  x  '.strip() == b'x' and b'xxhixx'.strip(b'x') == b'hi' and b'  x  '.lstrip() == b'x  ' and b'  x  '.rstrip() == b'  x'
assert b'xxhi'.lstrip(b'x') == b'hi' and b'hixx'.rstrip(b'x') == b'hi' and b'\t\n x'.strip() == b'x'
assert b'Hello'.upper() == b'HELLO' and b'Hello'.lower() == b'hello' and b'\xc3\xa9'.upper() == b'\xc3\xa9'   # ASCII-only
try:
    b','.join(['a'])
    assert False
except TypeError:
    pass
try:
    b'abc'.split(b'')
    assert False
except ValueError:
    pass

# ---- int <-> bytes ----
assert (1024).to_bytes(2, 'big') == b'\x04\x00' and (1024).to_bytes(2, 'little') == b'\x00\x04'
assert (255).to_bytes(1, 'big') == b'\xff' and (0).to_bytes(1, 'big') == b'\x00' and (1).to_bytes(4, 'big') == b'\x00\x00\x00\x01'
assert (-1).to_bytes(2, 'big', signed=True) == b'\xff\xff' and (-2).to_bytes(1, 'little', signed=True) == b'\xfe'
assert (2 ** 64).to_bytes(9, 'big') == b'\x01' + b'\x00' * 8 and (1).to_bytes() == b'\x01' and (258).to_bytes(2) == b'\x01\x02'
assert (1).to_bytes(2, byteorder='little') == b'\x01\x00'
try:
    (256).to_bytes(1, 'big')
    assert False
except OverflowError:
    pass
try:
    (-1).to_bytes(1, 'big')
    assert False
except OverflowError:
    pass
try:
    (1).to_bytes(1, 'middle')
    assert False
except ValueError:
    pass
assert int.from_bytes(b'\x04\x00', 'big') == 1024 and int.from_bytes(b'\x04\x00', 'little') == 4
assert int.from_bytes(b'\xff\xff', 'big', signed=True) == -1 and int.from_bytes(b'\xff\xff', 'big') == 65535
assert int.from_bytes(b'', 'big') == 0 and int.from_bytes(b'\x01' + b'\x00' * 8, 'big') == 2 ** 64
assert int.from_bytes(b'\x01\x02') == 258 and int.from_bytes([1, 2], 'big') == 258
assert int.from_bytes((1234567).to_bytes(4, 'little'), 'little') == 1234567
assert int.from_bytes(b'\xfe', 'little', signed=True) == -2

# ---- str <-> bytes round trips ----
for text in ('abc', 'é', '☺', '日本語', '', 'a\x00b'):
    assert text.encode('utf-8').decode('utf-8') == text
for text in ('abc', 'é', 'ÿ', ''):
    assert text.encode('latin-1').decode('latin-1') == text
assert len('日本語'.encode('utf-8')) == 9 and len('é'.encode('latin-1')) == 1

# ---- iteration and comprehension ----
assert [c for c in b'ab'] == [97, 98] and bytes(c + 1 for c in b'ab') == b'bc' and bytes(reversed(b'ab')) == b'ba'
assert b'abc'.decode().upper().encode() == b'ABC'
assert bytes([x ^ 0xFF for x in b'\x00\xff']) == b'\xff\x00'

# ---- cp1251 / cp1252 / utf-16 / utf-8-sig ----
assert 'привет'.encode('cp1251') == b'\xef\xf0\xe8\xe2\xe5\xf2'
assert b'\xef\xf0\xe8\xe2\xe5\xf2'.decode('cp1251') == 'привет'
assert str(b'\xef\xf0\xe8', 'cp1251') == 'при' and 'при'.encode('windows-1251') == b'\xef\xf0\xe8'
assert '€'.encode('cp1252') == b'\x80' and b'\x80'.decode('cp1252') == '€'
assert '€'.encode('cp1251') == b'\x88'                 # cp1251 carries the euro sign at 0x88
assert 'café'.encode('cp1252') == b'caf\xe9' and '€'.encode('windows-1252') == b'\x80'
assert 'a'.encode('utf-16') == b'\xff\xfea\x00' and b'\xff\xfea\x00'.decode('utf-16') == 'a'
assert b'\xfe\xff\x00a'.decode('utf-16') == 'a'        # BOM selects the byte order
assert b'a\x00'.decode('utf-16') == 'a'                # no BOM: little-endian
assert 'a'.encode('utf-16-le') == b'a\x00' and 'a'.encode('utf-16-be') == b'\x00a'
assert b'\x00a'.decode('utf-16-be') == 'a' and 'ш'.encode('utf-16-be') == b'\x04H'
assert 'abc'.encode('utf_16_le') == b'a\x00b\x00c\x00'
assert 'a'.encode('utf-8-sig') == b'\xef\xbb\xbfa'
assert b'\xef\xbb\xbfa'.decode('utf-8-sig') == 'a' and b'a'.decode('utf-8-sig') == 'a'

# the undefined positions of the Windows pages have no mapping, as in CPython
try:
    b'ab\x98'.decode('cp1251')
    assert False
except UnicodeDecodeError as e:
    assert str(e) == "'cp1251' codec can't decode byte 0x98 in position 2: character maps to <undefined>"
    assert (e.encoding, e.start, e.end, e.reason) == ('cp1251', 2, 3, 'character maps to <undefined>')
try:
    b'\x81'.decode('cp1252')
    assert False
except UnicodeDecodeError:
    pass
try:
    'ш'.encode('cp1252')
    assert False
except UnicodeEncodeError as e:
    assert str(e) == "'cp1252' codec can't encode character '\\u0448' in position 0: character maps to <undefined>"
try:
    b'abc'.decode('utf-16')
    assert False
except UnicodeDecodeError as e:
    assert (e.start, e.end, e.reason) == (2, 3, 'truncated data')

# errors= applies to the new codecs and to encode(), not just decode()
assert b'\x98'.decode('cp1251', 'replace') == '�' and b'\x98'.decode('cp1251', 'ignore') == ''
assert 'ш'.encode('cp1252', 'replace') == b'?' and 'ш'.encode('cp1252', 'ignore') == b''
assert 'éx'.encode('ascii', 'replace') == b'?x' and 'éx'.encode('ascii', 'ignore') == b'x'

# ---- the text-shaped methods: ASCII semantics, as CPython's bytes has ----
assert b'a=b=c'.partition(b'=') == (b'a', b'=', b'b=c')
assert b'a=b=c'.rpartition(b'=') == (b'a=b', b'=', b'c')
assert b'abc'.partition(b'=') == (b'abc', b'', b'') and b'abc'.rpartition(b'=') == (b'', b'', b'abc')
nl, cr, tab, vt = chr(10).encode(), chr(13).encode(), chr(9).encode(), chr(11).encode()
assert (b'a' + nl + b'b' + cr + nl + b'c').splitlines() == [b'a', b'b', b'c']
assert (b'a' + nl + b'b').splitlines(True) == [b'a' + nl, b'b']
assert (b'a' + vt + b'b').splitlines() == [b'a' + vt + b'b']   # bytes breaks on \n and \r only
assert b''.splitlines() == [] and (b'a' + nl).splitlines() == [b'a']
assert b'ab'.center(6, b'-') == b'--ab--' and b'ab'.center(5, b'-') == b'--ab-'
assert b'ab'.ljust(4, b'.') == b'ab..' and b'ab'.rjust(4, b'.') == b'..ab' and b'abcd'.ljust(2) == b'abcd'
assert b'1'.zfill(3) == b'001' and b'-1'.zfill(3) == b'-01' and b'+1'.zfill(4) == b'+001'
assert (b'a' + tab + b'b').expandtabs(4) == b'a   b' and (b'ab' + tab + b'c').expandtabs() == b'ab      c'
assert b'abcdef'.removeprefix(b'abc') == b'def' and b'abcdef'.removesuffix(b'def') == b'abc'
assert b'abcdef'.removeprefix(b'x') == b'abcdef'
table = bytes.maketrans(b'ab', b'xy')
assert len(table) == 256 and b'abc'.translate(table) == b'xyc'
assert b'abc'.translate(None, b'b') == b'ac' and b'abc'.translate(table, b'c') == b'xy'
assert b'aBC'.capitalize() == b'Abc' and b'hello world'.title() == b'Hello World' and b'aBc'.swapcase() == b'AbC'
assert b'abc'.isascii() and b''.isascii() and not bytes([255]).isascii()
assert b'abc'.isalpha() and not b''.isalpha() and b'123'.isdigit() and b'a1'.isalnum()
assert (b' ' + tab).isspace() and b'abc'.islower() and b'ABC'.isupper() and not b'123'.islower()
assert b'Hello World'.istitle() and not b'Hello world'.istitle()
try:
    b'abc'.partition(b'')
    assert False
except ValueError:
    pass
try:
    b'abc'.translate(b'short')
    assert False
except ValueError:
    pass

# bool carries int's method surface, as CPython's int subclass does; every answer is an int, never
# the bool back, and bool inherits the classmethod too
assert True.bit_length() == 1 and False.bit_length() == 0 and True.bit_count() == 1
assert True.to_bytes(1, 'big') == b'\x01' and False.to_bytes(2, 'big') == b'\x00\x00'
assert True.real == 1 and True.imag == 0 and True.numerator == 1 and True.denominator == 1
assert True.conjugate() == 1 and True.as_integer_ratio() == (1, 1)
assert bool.from_bytes(b'\x01', 'big') == 1
assert type(True.real).__name__ == 'int' and type(True).__name__ == 'bool' and repr(True) == 'True'
assert isinstance(True, int) and hash(True) == hash(1) and 'bit_length' in dir(True)

# ---- cp866 / koi8-r / iso-8859-5 (closed 2026-09-11): the other Cyrillic pages an integration meets ----
# known positions from CPython's tables; every one of the 256 positions is defined in each
assert 'Жир'.encode('cp866') == b'\x86\xa8\xe0' and b'\x86\xa8\xe0'.decode('cp866') == 'Жир'
assert 'Жир'.encode('koi8-r') == b'\xf6\xc9\xd2' and b'\xf6\xc9\xd2'.decode('koi8-r') == 'Жир'
assert 'Жир'.encode('iso-8859-5') == b'\xb6\xd8\xe0' and b'\xb6\xd8\xe0'.decode('iso-8859-5') == 'Жир'
assert b'\xf0\xf1\xff\xb0'.decode('cp866') == 'Ёё\xa0░'
assert b'\xa3\xb3\x9a'.decode('koi8-r') == 'ёЁ\xa0'
assert b'\xa1\xf1\xfd\xf0'.decode('iso-8859-5') == 'Ёё§№'
for _enc in ('cp866', 'koi8-r', 'iso-8859-5'):
    assert bytes(range(256)).decode(_enc).encode(_enc) == bytes(range(256)), _enc
assert b'\x80'.decode('866') == b'\x80'.decode('ibm866') == b'\x80'.decode('CP866') == 'А'
assert b'\xc1'.decode('koi8_r') == b'\xc1'.decode('cskoi8r') == 'а'
assert b'\xd0'.decode('iso8859_5') == b'\xd0'.decode('cyrillic') == 'а'
try:
    '€'.encode('cp866')
    assert False
except UnicodeEncodeError as e:
    assert str(e) == "'cp866' codec can't encode character '\\u20ac' in position 0: character maps to <undefined>"
assert '€'.encode('cp866', 'replace') == b'?'
