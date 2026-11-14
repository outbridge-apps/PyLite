# deadline: 3000
# allocmb: 1024
a = 'a' * 16000000
b = 'a' * 15999999 + 'b'
l = [a, b] * 1000
l.sort()
