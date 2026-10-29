# Adapted from CPython's test suite (Lib/test/test_codeccallbacks.py, Lib/test/test_unicode.py
# test_codecs_errors). Python Software Foundation, PSF License Version 2 -- see README.
# The error-handler set every codec here honours: strict, ignore, replace, backslashreplace, namereplace,
# xmlcharrefreplace (encode only) and surrogateescape.

# ---- backslashreplace ----
assert 'xé'.encode('ascii', 'backslashreplace') == b'x\\xe9'
assert 'xш'.encode('ascii', 'backslashreplace') == b'x\\u0448'
assert 'ш'.encode('cp1252', 'backslashreplace') == b'\\u0448'
assert '\U0001f600'.encode('ascii', 'backslashreplace') == b'\\U0001f600'   # one code point, not two units
assert b'a\xff\xfeb'.decode('utf-8', 'backslashreplace') == 'a\\xff\\xfeb'
assert b'\x98'.decode('cp1251', 'backslashreplace') == '\\x98'

# ---- xmlcharrefreplace: encode only ----
assert 'é'.encode('ascii', 'xmlcharrefreplace') == b'&#233;'
assert '\U0001f600'.encode('ascii', 'xmlcharrefreplace') == b'&#128512;'
try:
    b'\xff'.decode('utf-8', 'xmlcharrefreplace')
    assert False
except TypeError:
    pass

# ---- surrogateescape: the round trip is the point ----
assert b'a\xffb'.decode('utf-8', 'surrogateescape') == 'a\udcffb'
assert b'a\xff\x80b'.decode('utf-8', 'surrogateescape').encode('utf-8', 'surrogateescape') == b'a\xff\x80b'
assert b'\xe9'.decode('ascii', 'surrogateescape').encode('ascii', 'surrogateescape') == b'\xe9'
assert b'\xef\xbb\xbf\xff'.decode('utf-8-sig', 'surrogateescape') == '\udcff'
try:
    'é'.encode('ascii', 'surrogateescape')          # only bytes it could have escaped round-trip
    assert False
except UnicodeEncodeError:
    pass

# ---- replace: one replacement per malformed run, not per byte ----
assert b'\xff\xff'.decode('utf-8', 'replace') == '��'
assert b'\xe0\xa0'.decode('utf-8', 'replace') == '�'
assert b'a\xffb'.decode('utf-8', 'ignore') == 'ab'

# ---- a lone surrogate has no utf-8 / utf-16 encoding ----
try:
    '\udc80'.encode('utf-8')
    assert False
except UnicodeEncodeError as e:
    assert str(e) == "'utf-8' codec can't encode character '\\udc80' in position 0: surrogates not allowed"
try:
    '\ud800'.encode('utf-16-le')
    assert False
except UnicodeEncodeError:
    pass
try:
    b'\x00\xd8'.decode('utf-16-le')
    assert False
except UnicodeDecodeError as e:
    assert str(e) == "'utf-16-le' codec can't decode bytes in position 0-1: illegal encoding"
assert '\ud800'.encode('utf-16-le', 'replace') == b'?\x00'   # the replacement is re-encoded as units
assert '\U0001f600'.encode('utf-16-le').decode('utf-16-le') == '\U0001f600'   # valid pairs are untouched

# ---- an unknown handler is a LookupError, resolved only when something fails (as in CPython) ----
assert 'abc'.encode('ascii', 'bogus') == b'abc' and b'abc'.decode('utf-8', 'bogus') == 'abc'
try:
    'é'.encode('ascii', 'bogus')
    assert False
except LookupError as e:
    assert str(e) == "unknown error handler name 'bogus'"
try:
    b'\xff'.decode('utf-8', 'bogus')
    assert False
except LookupError:
    pass

# ---- namereplace: the name of a character that has one, the backslash escape of one that does not ----
BS = chr(92).encode('ascii')
assert 'x' + chr(0x20AC) == 'x€' and ('x' + chr(0x20AC)).encode('ascii', 'namereplace') == b'x' + BS + b'N{EURO SIGN}'
assert chr(0x1F600).encode('ascii', 'namereplace') == BS + b'N{GRINNING FACE}'
assert chr(0x1A2).encode('ascii', 'namereplace') == BS + b'N{LATIN CAPITAL LETTER OI}'        # the name, not the GHA alias
assert chr(0xAC01).encode('ascii', 'namereplace') == BS + b'N{HANGUL SYLLABLE GAG}'
assert chr(0x4E00).encode('ascii', 'namereplace') == BS + b'N{CJK UNIFIED IDEOGRAPH-4E00}'
assert chr(0xE000).encode('ascii', 'namereplace') == BS + b'ue000'                               # private use has no name
assert chr(0x448).encode('cp1252', 'namereplace') == BS + b'N{CYRILLIC SMALL LETTER SHA}'
assert 'abc'.encode('ascii', 'namereplace') == b'abc'
try:
    b'\xff'.decode('utf-8', 'namereplace')
    assert False
except TypeError:
    pass
