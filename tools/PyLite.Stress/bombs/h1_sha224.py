# deadline: 20000
# allocmb: 1024
import hashlib
print(hashlib.sha224(b'x' * 8000000).hexdigest()[:8])
