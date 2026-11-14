# Review 12, row 13 follow-up: is_normalized stops once the form has outgrown the string.
import unicodedata
s = chr(0xfdfa) * 200000
print(unicodedata.is_normalized('NFKC', s), unicodedata.is_normalized('NFKD', s))
print(unicodedata.is_normalized('NFC', 'abc' * 1000), unicodedata.is_normalized('NFD', chr(0x439)), unicodedata.is_normalized('NFC', chr(0x439)))
print(unicodedata.is_normalized('NFC', 'a' + chr(0x301)), unicodedata.is_normalized('NFD', 'a' + chr(0x301)), unicodedata.is_normalized('NFKC', chr(0xfb01)))
