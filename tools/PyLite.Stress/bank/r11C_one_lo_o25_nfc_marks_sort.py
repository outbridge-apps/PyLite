# allocmb: 24
# deadline: 1500
import unicodedata
print(len(unicodedata.normalize("NFC", ("a" + "".join(chr(0x300 + i % 0x6F) for i in range(200))) * 20000)))
