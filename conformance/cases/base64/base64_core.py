# Adapted from CPython's test suite (Lib/test/test_base64.py; vectors from RFC 4648).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import base64

# RFC 4648 vectors
assert base64.b64encode(b'') == b''
assert base64.b64encode(b'f') == b'Zg=='
assert base64.b64encode(b'fo') == b'Zm8='
assert base64.b64encode(b'foo') == b'Zm9v'
assert base64.b64encode(b'foobar') == b'Zm9vYmFy'

# decode and round trip
assert base64.b64decode(b'Zm9vYmFy') == b'foobar'
data = 'какой-то текст'.encode('utf-8')
assert base64.b64decode(base64.b64encode(data)) == data

# urlsafe variant swaps +/ for -_
raw = bytes([251, 239, 190])
std = base64.b64encode(raw)
url = base64.urlsafe_b64encode(raw)
assert b'+' in std or b'/' in std
assert b'+' not in url and b'/' not in url
assert base64.urlsafe_b64decode(url) == raw

# b16 is plain hex, uppercase
assert base64.b16encode(b'\x01\xab') == b'01AB'
assert base64.b16decode(b'01AB') == b'\x01\xab'

# b32
assert base64.b32encode(b'abc') == b'MFRGG==='
assert base64.b32decode(b'MFRGG===') == b'abc'
