# deadline: 20000
# allocmb: 1024
import unicodedata
n = 0
for i in range(300000):
    n += len(unicodedata.name(chr(0x400 + i % 200), ''))
print(n)
