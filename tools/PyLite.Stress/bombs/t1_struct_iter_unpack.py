# deadline: 20000
# allocmb: 1024
import struct
print(sum(1 for _ in struct.iter_unpack('i', bytes(4 * 2000000))))
