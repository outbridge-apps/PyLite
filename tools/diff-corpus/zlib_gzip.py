import zlib
import gzip
data = b'the quick brown fox jumps over the lazy dog ' * 40
z = zlib.decompress(zlib.compress(data))
zbest = zlib.decompress(zlib.compress(data, 9))
g = gzip.decompress(gzip.compress(data))
result = [zlib.crc32(data), zlib.adler32(data), z == data, zbest == data, g == data, len(data)]
