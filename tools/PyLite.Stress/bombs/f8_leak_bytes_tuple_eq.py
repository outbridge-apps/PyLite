# deadline: 3000
# allocmb: 256
a = b'a' * 16000000
b = b'a' * 15999999 + b'a'
x = (a,) * 1000 == (b,) * 1000
