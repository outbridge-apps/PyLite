# deadline: 20000
# allocmb: 64
import gzip
z = gzip.compress(bytes(50000000))
print(len(z))
try:
    print(len(gzip.decompress(z)))
except MemoryError as e:
    print('clean MemoryError')
