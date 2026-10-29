# deadline: 20000
# allocmb: 64
import zlib
z = zlib.compress(bytes(50000000))
try:
    print(len(zlib.decompress(z)))
except MemoryError as e:
    print('clean MemoryError')
