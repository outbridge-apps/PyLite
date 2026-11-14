# deadline: 1500
import unicodedata
print(len(unicodedata.normalize("NFD", chr(0x1E9B) + chr(0x323) * 3000000)))
