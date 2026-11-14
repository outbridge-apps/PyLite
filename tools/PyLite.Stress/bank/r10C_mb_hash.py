# repeat: 2
# deadline: 60000
# allocmb: 2000
import time, hashlib
t = time.perf_counter()
b = b"x" * 20000
for _ in range(300): hashlib.md5(b).hexdigest(); hashlib.sha256(b).hexdigest(); hashlib.sha1(b).digest()
print("ms=%d" % ((time.perf_counter() - t) * 1000))
