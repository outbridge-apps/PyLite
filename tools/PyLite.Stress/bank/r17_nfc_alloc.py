# deadline: 60000
# Review 12, row 17: NFC of 6M characters; the code point buffer is one piece's, not the text's.
import unicodedata as u
base = 'Cafe' + chr(0x301) + ' r' + chr(0xe9) + 'sum' + chr(0xe9) + ' na' + chr(0xef) + 've ' + chr(0xfb01) + 'n ' + chr(0x2460) + ' '
s = base * 250000
r = u.normalize('NFC', s)
print(len(s), len(r))
