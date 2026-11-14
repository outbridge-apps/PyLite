# deadline: 3000
# allocmb: 1024
import hmac
h = hmac.new(b'k' * 16000000, digestmod='sha256')
while True:
    h.update(b'')
    h.digest()
