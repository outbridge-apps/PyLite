# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, struct
t = time.perf_counter()
for i in range(50000): struct.unpack("<iqd", struct.pack("<iqd", i, i, i))
print("ms=%d" % ((time.perf_counter() - t) * 1000))
