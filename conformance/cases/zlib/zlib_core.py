# Adapted from CPython's test suite (Lib/test/test_zlib.py: ChecksumTestCase, CompressTestCase).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# The dialect exposes one-shot compress/decompress + checksums only (docs/deviations.md "zlib / gzip").

import zlib

# ---- checksums: implementation-independent vectors ----
assert zlib.crc32(b'') == 0
assert zlib.crc32(b'a') == 3904355907
assert zlib.crc32(b'abc') == 891568578
assert zlib.crc32(b'123456789') == 3421780262
assert zlib.crc32(b'hello') == 907060870
assert zlib.adler32(b'') == 1
assert zlib.adler32(b'abc') == 38600999
assert zlib.adler32(b'Wikipedia') == 300286872

# a running checksum equals the one-shot checksum
assert zlib.crc32(b'456789', zlib.crc32(b'123')) == zlib.crc32(b'123456789')
assert zlib.adler32(b'pedia', zlib.adler32(b'Wiki')) == zlib.adler32(b'Wikipedia')

# results are unsigned 32-bit
assert 0 <= zlib.crc32(b'\xff' * 100) <= 0xFFFFFFFF
assert 0 <= zlib.adler32(b'\xff' * 100) <= 0xFFFFFFFF

# ---- constants ----
assert zlib.MAX_WBITS == 15
assert zlib.DEFLATED == 8
assert zlib.Z_DEFAULT_COMPRESSION == -1
assert zlib.Z_NO_COMPRESSION == 0
assert zlib.Z_BEST_SPEED == 1
assert zlib.Z_BEST_COMPRESSION == 9

# ---- round trips ----
data = b'The quick brown fox jumps over the lazy dog. ' * 50
z = zlib.compress(data)
assert isinstance(z, bytes)
assert z[0] == 0x78                      # zlib header CMF byte (deflate, 32K window)
assert len(z) < len(data)
assert zlib.decompress(z) == data

assert zlib.decompress(zlib.compress(b'')) == b''
one = b'x'
assert zlib.decompress(zlib.compress(one)) == one

# every level yields a valid stream that decodes to the input
for level in range(-1, 10):
    assert zlib.decompress(zlib.compress(data, level)) == data
assert zlib.decompress(zlib.compress(data, level=9)) == data
assert zlib.decompress(zlib.compress(data, zlib.Z_NO_COMPRESSION)) == data

# level 0 (stored) is not smaller than the input
stored = zlib.compress(data, 0)
assert len(stored) >= len(data)
assert zlib.decompress(stored) == data

# binary data survives
blob = bytes(range(256)) * 4
assert zlib.decompress(zlib.compress(blob)) == blob

# ---- wbits: container selection ----
raw = zlib.compress(data, 6, -15)           # raw deflate
assert raw[0] != 0x78
assert zlib.decompress(raw, -15) == data
gz = zlib.compress(data, 6, 31)             # gzip container
assert gz[:2] == b'\x1f\x8b'
assert zlib.decompress(gz, 31) == data
assert zlib.decompress(gz, 47) == data      # 32 + 15: auto-detect zlib or gzip
assert zlib.decompress(z, 47) == data
assert zlib.decompress(z, zlib.MAX_WBITS) == data
assert zlib.decompress(z, wbits=15) == data

# ---- errors ----
try:
    zlib.decompress(b'this is not a zlib stream')
    assert False
except zlib.error:
    pass

try:
    zlib.decompress(z[:-4])                # truncated: the adler32 trailer is missing
    assert False
except zlib.error:
    pass

try:
    zlib.compress('text')                  # str is not bytes-like
    assert False
except TypeError:
    pass

try:
    zlib.compress(data, 10)                # level out of range
    assert False
except (zlib.error, ValueError):
    pass

# zlib.error is an Exception
try:
    zlib.decompress(b'garbage')
except Exception as e:
    assert isinstance(e, zlib.error)
