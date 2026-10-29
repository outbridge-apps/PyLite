# Adapted from CPython's test suite (Lib/test/test_binascii.py: test_base64valid, test_hex, test_crc32).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import binascii

# ---- hex ----
assert binascii.hexlify(b'\x01\xab\xff') == b'01abff'
assert binascii.b2a_hex(b'\x01\xab\xff') == b'01abff'
assert binascii.hexlify(b'') == b''
assert binascii.unhexlify(b'01abff') == b'\x01\xab\xff'
assert binascii.a2b_hex(b'01ABFF') == b'\x01\xab\xff'        # upper case accepted
assert binascii.unhexlify('01abff') == b'\x01\xab\xff'       # str input accepted
assert binascii.unhexlify(binascii.hexlify(bytes(range(256)))) == bytes(range(256))
assert binascii.hexlify(b'abc') == b'abc'.hex().encode()

# odd length / non-hex digits
try:
    binascii.unhexlify(b'abc')
    assert False
except binascii.Error:
    pass
try:
    binascii.unhexlify(b'zz')
    assert False
except binascii.Error:
    pass

# ---- base64 ----
assert binascii.b2a_base64(b'foobar') == b'Zm9vYmFy\n'
assert binascii.b2a_base64(b'foobar', newline=False) == b'Zm9vYmFy'
assert binascii.b2a_base64(b'') == b'\n'
assert binascii.a2b_base64(b'Zm9vYmFy\n') == b'foobar'
assert binascii.a2b_base64(b'Zm9vYmFy') == b'foobar'
assert binascii.a2b_base64('Zm9vYmFy') == b'foobar'
assert binascii.a2b_base64(b'Zg==') == b'f'
assert binascii.a2b_base64(b'Zm8=') == b'fo'
assert binascii.a2b_base64(b'') == b''
# whitespace inside the input is ignored by the decoder
assert binascii.a2b_base64(b'Zm9v\nYmFy') == b'foobar'
blob = bytes(range(256))
assert binascii.a2b_base64(binascii.b2a_base64(blob)) == blob

try:
    binascii.a2b_base64(b'Zm9vY')          # incorrect padding
    assert False
except binascii.Error:
    pass

# ---- crc32 ----
assert binascii.crc32(b'') == 0
assert binascii.crc32(b'123456789') == 3421780262
assert binascii.crc32(b'abc') == 891568578
assert binascii.crc32(b'456789', binascii.crc32(b'123')) == 3421780262
assert 0 <= binascii.crc32(b'\xff' * 33) <= 0xFFFFFFFF

# ---- Error ----
assert issubclass(binascii.Error, ValueError)
try:
    binascii.unhexlify(b'0')
except ValueError as e:
    assert isinstance(e, binascii.Error)

try:
    binascii.hexlify('text')
    assert False
except TypeError:
    pass
