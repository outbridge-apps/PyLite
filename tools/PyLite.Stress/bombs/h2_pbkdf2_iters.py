# deadline: 20000
# allocmb: 1024
import hashlib
print(hashlib.pbkdf2_hmac('sha256', b'p', b's', 3000000).hex()[:8])
