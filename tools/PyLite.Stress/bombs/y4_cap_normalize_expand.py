# deadline: 3000
# allocmb: 64
import unicodedata
x = unicodedata.normalize('NFKD', chr(0xfdfa) * 1000000)
