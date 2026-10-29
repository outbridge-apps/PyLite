# deadline: 20000
# allocmb: 1024
b = bytes([54, 4]) * 4000000
print(len(b.decode('utf-16-le')))
