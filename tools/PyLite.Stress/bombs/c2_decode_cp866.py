# deadline: 20000
# allocmb: 1024
b = bytes([134]) * 8000000
print(len(b.decode('cp866')))
