import math
m, d = (1e300).as_integer_ratio()
print(d, m % 10, int(1e300) % 1000, math.floor(1e300) % 1000, m.bit_length(), str(m)[-20:], (1e300).hex())
m2 = int((1e300).hex()[4:17], 16) + (1 << 52)
print(m2, m2 % 5, (m2 << (996 - 52)) % 1000)
