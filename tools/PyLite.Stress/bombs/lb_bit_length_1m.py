# deadline: 20000
# allocmb: 1024
x = 1 << 999999
print(x.bit_length(), (x - 1).bit_count())
