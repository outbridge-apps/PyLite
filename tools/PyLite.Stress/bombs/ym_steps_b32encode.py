# steps: 10000
# deadline: 60000
# allocmb: 1024
import base64
s = b'a' * 9000000
for i in range(40):
    x = base64.b32encode(s)
