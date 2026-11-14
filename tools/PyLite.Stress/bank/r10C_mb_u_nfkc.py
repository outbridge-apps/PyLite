# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, unicodedata
t = time.perf_counter()
for _ in range(10000): unicodedata.normalize("NFKC", "Ａﬁ①" * 30)
print("ms=%d" % ((time.perf_counter() - t) * 1000))
