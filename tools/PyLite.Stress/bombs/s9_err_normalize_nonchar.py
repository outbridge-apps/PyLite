# deadline: 3000
# allocmb: 256
import unicodedata
s = chr(0xFFFF) * 4000000
print(unicodedata.normalize('NFC', s) == s)
