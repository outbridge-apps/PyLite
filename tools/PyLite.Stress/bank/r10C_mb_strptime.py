# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, datetime
t = time.perf_counter()
for i in range(20000): datetime.datetime.strptime("2024-05-%02d 11:22:33" % (i % 28 + 1), "%Y-%m-%d %H:%M:%S")
print("ms=%d" % ((time.perf_counter() - t) * 1000))
