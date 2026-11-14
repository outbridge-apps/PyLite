# deadline: 20000
# allocmb: 1024
import hashlib
h = hashlib.sha256()
for _ in range(1000000):
    h.update(b'a')
copies = [h.copy() for _ in range(8)]
print(copies[0].hexdigest() == h.hexdigest())
