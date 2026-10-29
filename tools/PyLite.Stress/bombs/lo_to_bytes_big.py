# deadline: 20000
# allocmb: 1024
x = 7 ** 280000
print(len(x.to_bytes((x.bit_length() + 7) // 8, 'big')))
