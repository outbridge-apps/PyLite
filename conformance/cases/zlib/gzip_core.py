# Adapted from CPython's test suite (Lib/test/test_gzip.py: TestGzip.test_compress/test_decompress,
# test_bad_gzip_file). Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# The dialect exposes one-shot compress/decompress only (docs/deviations.md "zlib / gzip");
# multi-member streams ARE supported.

import gzip
import zlib

data = b'This is a test of the gzip module. ' * 40

# ---- round trip and container shape ----
c = gzip.compress(data)
assert isinstance(c, bytes)
assert c[:2] == b'\x1f\x8b'          # gzip magic
assert c[2] == 8                     # CM = deflate
assert len(c) < len(data)
assert gzip.decompress(c) == data

assert gzip.decompress(gzip.compress(b'')) == b''
blob = bytes(range(256)) * 3
assert gzip.decompress(gzip.compress(blob)) == blob

# every compresslevel produces a valid stream
for level in range(0, 10):
    assert gzip.decompress(gzip.compress(data, level)) == data
assert gzip.decompress(gzip.compress(data, compresslevel=1)) == data

# ---- mtime ----
# default mtime is 0 in the dialect (deterministic); bytes 4..8 hold it little-endian
assert c[4:8] == b'\x00\x00\x00\x00'
stamped = gzip.compress(data, mtime=1)
assert stamped[4:8] == b'\x01\x00\x00\x00'
assert gzip.decompress(stamped) == data
stamped2 = gzip.compress(data, mtime=0x01020304)
assert stamped2[4:8] == b'\x04\x03\x02\x01'

# the trailer carries CRC32 and ISIZE (little-endian)
tail = gzip.compress(b'abc')
crc = zlib.crc32(b'abc')
assert tail[-8:-4] == bytes([crc & 0xFF, (crc >> 8) & 0xFF, (crc >> 16) & 0xFF, (crc >> 24) & 0xFF])
assert tail[-4:] == b'\x03\x00\x00\x00'

# ---- interop with zlib's gzip wbits ----
assert zlib.decompress(gzip.compress(data), 31) == data
assert zlib.decompress(gzip.compress(data), 47) == data
assert gzip.decompress(zlib.compress(data, 6, 31)) == data

# ---- errors ----
try:
    gzip.decompress(b'not a gzip stream at all')
    assert False
except gzip.BadGzipFile:
    pass

try:
    gzip.decompress(zlib.compress(data))     # a zlib stream is not gzip
    assert False
except gzip.BadGzipFile:
    pass

# a corrupted trailer CRC is detected
bad = bytearray_free = c[:-8] + b'\x00\x00\x00\x00' + c[-4:]
try:
    gzip.decompress(bad)
    assert False
except gzip.BadGzipFile:
    pass

# BadGzipFile derives from OSError
try:
    gzip.decompress(b'\x1f\x8b\x08garbage')
except OSError as e:
    assert isinstance(e, gzip.BadGzipFile) and issubclass(gzip.BadGzipFile, OSError) and issubclass(OSError, Exception)

try:
    gzip.compress('str')
    assert False
except TypeError:
    pass

# ---- multi-member streams are concatenated, as in CPython ----
m1 = gzip.compress(b'a')
m2 = gzip.compress(b'b')
m0 = gzip.compress(b'')
assert gzip.decompress(m1 + m2) == b'ab'
assert gzip.decompress(m0 + gzip.compress(b'x')) == b'x'
assert gzip.decompress(m1 + m2 + m0 + gzip.compress(b'cd')) == b'abcd'
assert gzip.decompress(m1) == b'a' and gzip.decompress(m0) == b''
assert zlib.decompress(m1 + m2, 47) == b'ab' and zlib.decompress(m1 + m2, 31) == b'ab'
big = gzip.compress(data * 20)
assert gzip.decompress(big + big) == data * 40
# member boundaries are found by validating trailers, so a payload that CONTAINS the gzip magic
# is not mistaken for a boundary
assert gzip.decompress(gzip.compress(b'\x1f\x8b\x08' * 40)) == b'\x1f\x8b\x08' * 40
