# Adapted from CPython's test suite (Lib/test/test_struct.py).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

import struct

# little-endian int round trip
packed = struct.pack('<i', 1234567)
assert len(packed) == 4
assert struct.unpack('<i', packed) == (1234567,)

# big vs little endian byte order
assert struct.pack('>H', 1) == b'\x00\x01'
assert struct.pack('<H', 1) == b'\x01\x00'
assert struct.unpack('>H', b'\x01\x00') == (256,)

# multiple fields
p = struct.pack('<hhl', 1, 2, 3)
assert struct.unpack('<hhl', p) == (1, 2, 3)

# calcsize
assert struct.calcsize('<i') == 4
assert struct.calcsize('<h') == 2
assert struct.calcsize('<b') == 1
assert struct.calcsize('<d') == 8

# floats survive the round trip
f = struct.unpack('<d', struct.pack('<d', 2.5))[0]
assert f == 2.5

# signed vs unsigned
assert struct.unpack('<b', b'\xff') == (-1,)
assert struct.unpack('<B', b'\xff') == (255,)

# bytes payloads
assert struct.pack('<3s', b'abc') == b'abc'
assert struct.unpack('<3s', b'abc') == (b'abc',)

# error on wrong buffer size
raised = False
try:
    struct.unpack('<i', b'\x00')
except Exception:
    raised = True
assert raised
