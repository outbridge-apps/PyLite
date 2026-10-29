# Adapted from CPython's test suite (Lib/test/test_struct.py: StructTest.test_consistence/test_transitiveness/
# test_new_features/test_calcsize/test_integers/test_p_code/test_705836/test_unpack_from/test_bool/test_705836).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: standard sizes only (@ is treated like =: no native alignment), codes x c b B ? h H i I l L q Q f d s.

import struct

# ---- calcsize and byte-order prefixes ----
assert struct.calcsize('b') == 1 and struct.calcsize('h') == 2 and struct.calcsize('i') == 4 and struct.calcsize('q') == 8
assert struct.calcsize('f') == 4 and struct.calcsize('d') == 8 and struct.calcsize('?') == 1 and struct.calcsize('c') == 1
assert struct.calcsize('x') == 1 and struct.calcsize('5s') == 5 and struct.calcsize('0s') == 0 and struct.calcsize('') == 0
assert struct.calcsize('<ihq') == 14 and struct.calcsize('>ihq') == 14 and struct.calcsize('!ihq') == 14 and struct.calcsize('=ihq') == 14
assert struct.calcsize('3i') == 12 and struct.calcsize('2h3b') == 7 and struct.calcsize('< i h') == 6 and struct.calcsize('l') == 4 and struct.calcsize('L') == 4
assert struct.calcsize('10x') == 10 and struct.calcsize('IH?') == 7

# ---- integers, both endiannesses ----
assert struct.pack('<h', -2) == b'\xfe\xff' and struct.pack('>h', -2) == b'\xff\xfe' and struct.pack('!h', 1) == b'\x00\x01'
assert struct.pack('<I', 1) == b'\x01\x00\x00\x00' and struct.pack('>I', 1) == b'\x00\x00\x00\x01' and struct.pack('<i', -1) == b'\xff\xff\xff\xff'
assert struct.pack('<q', 2 ** 63 - 1) == b'\xff\xff\xff\xff\xff\xff\xff\x7f' and struct.pack('<Q', 2 ** 64 - 1) == b'\xff' * 8
assert struct.pack('>q', -(2 ** 63)) == b'\x80' + b'\x00' * 7 and struct.pack('<b', -128) == b'\x80' and struct.pack('B', 255) == b'\xff'
assert struct.pack('<H', 65535) == b'\xff\xff' and struct.pack('<l', -2) == b'\xfe\xff\xff\xff' and struct.pack('<L', 4294967295) == b'\xff' * 4
assert struct.pack('<hh', 1, 2) == b'\x01\x00\x02\x00' and struct.pack('<2h', 1, 2) == b'\x01\x00\x02\x00' and struct.pack('<h2xb', 1, 2) == b'\x01\x00\x00\x00\x02'
assert struct.pack('b', True) == b'\x01' and struct.pack('<i', 2 ** 31 - 1) == b'\xff\xff\xff\x7f'
assert struct.unpack('<h', b'\xfe\xff') == (-2,) and struct.unpack('>h', b'\xff\xfe') == (-2,) and struct.unpack('<I', b'\x01\x00\x00\x00') == (1,)
assert struct.unpack('>I', b'\x00\x00\x00\x01') == (1,) and struct.unpack('<Q', b'\xff' * 8) == (2 ** 64 - 1,) and struct.unpack('<q', b'\xff' * 8) == (-1,)
assert struct.unpack('<hh', b'\x01\x00\x02\x00') == (1, 2) and struct.unpack('<b', b'\x80') == (-128,) and struct.unpack('B', b'\x80') == (128,)
assert struct.unpack('<h2xb', b'\x01\x00\x00\x00\x02') == (1, 2) and struct.unpack('', b'') == () and struct.unpack('<2xH', b'\xff\xff\x01\x00') == (1,)
assert isinstance(struct.unpack('<i', b'\x01\x00\x00\x00')[0], int) and isinstance(struct.unpack('<h', b'\x01\x00'), tuple)

# ---- floats ----
assert struct.pack('<f', 1.0) == b'\x00\x00\x80\x3f' and struct.pack('>f', 1.0) == b'\x3f\x80\x00\x00' and struct.pack('<d', 1.0) == b'\x00\x00\x00\x00\x00\x00\xf0\x3f'
assert struct.pack('>d', -2.5) == b'\xc0\x04\x00\x00\x00\x00\x00\x00' and struct.unpack('<f', b'\x00\x00\x80\x3f') == (1.0,) and struct.unpack('>d', b'\x40\x09\x21\xfb\x54\x44\x2d\x18')[0] == 3.141592653589793
assert struct.unpack('<f', struct.pack('<f', 0.1))[0] != 0.1 and abs(struct.unpack('<f', struct.pack('<f', 0.1))[0] - 0.1) < 1e-7
assert struct.unpack('<d', struct.pack('<d', 0.1)) == (0.1,) and struct.pack('<d', float('inf')) == b'\x00\x00\x00\x00\x00\x00\xf0\x7f'
assert struct.unpack('<d', struct.pack('<d', float('inf')))[0] == float('inf') and struct.unpack('<f', struct.pack('<f', -0.0))[0] == 0.0
nan = struct.unpack('<d', struct.pack('<d', float('nan')))[0]
assert nan != nan and struct.pack('<d', 3) == struct.pack('<d', 3.0) and struct.pack('<f', True) == struct.pack('<f', 1.0)
try:
    struct.pack('<f', 1e40)
    assert False
except OverflowError:
    pass

# ---- bool, char, bytes, padding ----
assert struct.pack('?', True) == b'\x01' and struct.pack('?', False) == b'\x00' and struct.pack('?', 5) == b'\x01' and struct.pack('?', []) == b'\x00'
assert struct.unpack('?', b'\x01') == (True,) and struct.unpack('?', b'\x00') == (False,) and struct.unpack('?', b'\x07') == (True,)
assert struct.pack('c', b'x') == b'x' and struct.unpack('c', b'x') == (b'x',) and struct.pack('2c', b'a', b'b') == b'ab'
assert struct.pack('5s', b'ab') == b'ab\x00\x00\x00' and struct.pack('2s', b'abcd') == b'ab' and struct.pack('s', b'xyz') == b'x' and struct.pack('0s', b'abc') == b''
assert struct.unpack('5s', b'ab\x00\x00\x00') == (b'ab\x00\x00\x00',) and struct.unpack('3s', b'abc') == (b'abc',) and struct.unpack('0s', b'') == (b'',)
assert struct.pack('xbx', 1) == b'\x00\x01\x00' and struct.unpack('xbx', b'\x00\x01\x00') == (1,) and struct.pack('3x') == b'\x00\x00\x00'
assert struct.pack('<h3sb?', 7, b'ok', 3, True) == b'\x07\x00ok\x00\x03\x01'
assert struct.unpack('<h3sb?', b'\x07\x00ok\x00\x03\x01') == (7, b'ok\x00', 3, True)

# ---- unpack_from ----
buf = b'\xaa' + struct.pack('<hi', 1, 2) + b'\xbb'
assert struct.unpack_from('<hi', buf, 1) == (1, 2) and struct.unpack_from('<h', buf, offset=1) == (1,) and struct.unpack_from('<h', buf[1:]) == (1,)
assert struct.unpack_from('B', buf) == (0xaa,) and struct.unpack_from('B', buf, -1) == (0xbb,) and struct.unpack_from('<i', buf, 3) == (2,)
try:
    struct.unpack_from('<hi', buf, 3)           # 6 bytes needed, 5 left
    assert False
except struct.error:
    pass
try:
    struct.unpack_from('<h', buf, 10)
    assert False
except struct.error:
    pass

# ---- round trips ----
for fmt, values in (('<bBhHiIlLqQ', (-1, 255, -2, 65535, -3, 4294967295, -4, 4294967295, -5, 2 ** 64 - 1)),
                    ('>bBhHiIlLqQ', (127, 0, 32767, 0, 2 ** 31 - 1, 0, -(2 ** 31), 1, 2 ** 63 - 1, 0)),
                    ('!?fd', (True, 0.5, -1e300)), ('<3s2xh', (b'abc', 9)), ('=cxb', (b'z', -7))):
    packed = struct.pack(fmt, *values)
    assert len(packed) == struct.calcsize(fmt) and struct.unpack(fmt, packed) == values
assert struct.unpack('<HH', struct.pack('<I', 0x00020001)) == (1, 2) and struct.unpack('>HH', struct.pack('>I', 0x00010002)) == (1, 2)
assert struct.pack('<h', 258) == (258).to_bytes(2, 'little') and struct.unpack('>I', (7).to_bytes(4, 'big')) == (7,)
assert struct.pack('<Q', 123456789012345)[::-1] == struct.pack('>Q', 123456789012345)

# ---- errors ----
for bad in (lambda: struct.pack('<h', 32768), lambda: struct.pack('<h', -32769), lambda: struct.pack('B', 256), lambda: struct.pack('B', -1),
            lambda: struct.pack('<H', -1), lambda: struct.pack('<Q', -1), lambda: struct.pack('<q', 2 ** 63), lambda: struct.pack('<I', 2 ** 32),
            lambda: struct.pack('<h'), lambda: struct.pack('<h', 1, 2), lambda: struct.unpack('<h', b'\x01'), lambda: struct.unpack('<h', b'\x01\x00\x00'),
            lambda: struct.calcsize('z'), lambda: struct.pack('z', 1), lambda: struct.unpack('<h', b''), lambda: struct.pack('c', b'ab'),
            lambda: struct.pack('<2h', 1)):
    try:
        bad()
        assert False
    except struct.error:
        pass
for bad in (lambda: struct.pack('<h', 1.5), lambda: struct.pack('<h', '1'), lambda: struct.pack('<f', 'x'), lambda: struct.pack('5s', 'text'),
            lambda: struct.pack('c', 'x'), lambda: struct.unpack('<h', 'ab'), lambda: struct.pack(5, 1), lambda: struct.pack('<h', None)):
    try:
        bad()
        assert False
    except (struct.error, TypeError):
        pass
assert issubclass(struct.error, Exception)
try:
    struct.pack('<h', 32768)
except Exception as e:
    assert isinstance(e, struct.error) and str(e) != ''

# ---- idioms: a binary record header ----
header = struct.pack('<4sHHI', b'PYLT', 1, 0, 1024)
assert len(header) == 12 and header[:4] == b'PYLT'
magic, major, minor, size = struct.unpack('<4sHHI', header)
assert (magic, major, minor, size) == (b'PYLT', 1, 0, 1024)
records = b''.join(struct.pack('<Hf', i, i * 0.5) for i in range(3))
assert len(records) == 18 and [struct.unpack_from('<Hf', records, i * 6) for i in range(3)] == [(0, 0.0), (1, 0.5), (2, 1.0)]

# ---- iter_unpack: one tuple per record; a ragged buffer is an error ----
assert list(struct.iter_unpack('>h', struct.pack('>3h', 1, 2, 3))) == [(1,), (2,), (3,)]
assert list(struct.iter_unpack('<hh', struct.pack('<4h', 1, 2, 3, 4))) == [(1, 2), (3, 4)]
assert list(struct.iter_unpack('>h', b'')) == []
assert [t[0] for t in struct.iter_unpack('>B', b'abc')] == [97, 98, 99]
try:
    list(struct.iter_unpack('>h', b'abc'))
    assert False
except struct.error:
    pass
