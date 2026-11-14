# deadline: 3000
# allocmb: 1024
import hashlib
x = hashlib.pbkdf2_hmac('sha256', b'k', b's' * 16000000, 1, dklen=3200)
