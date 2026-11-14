# allocmb: 64
# deadline: 20000
import unicodedata
s = chr(0xFDFA) * 800000
r = unicodedata.normalize("NFKD", s)
print(len(r))
