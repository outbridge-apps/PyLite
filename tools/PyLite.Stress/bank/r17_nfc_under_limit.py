# deadline: 60000
# strchars: 1000000
# Review 12, row 13: NFC and is_normalized of a 600K text whose NFD (1.14M) is past the string limit.
import unicodedata as u
s = (chr(0xe9) * 9 + ' ') * 60000
print('isNFD', u.is_normalized('NFD', s))
t = s[:-2] + 'e' + chr(0x301)
print('isNFC', u.is_normalized('NFC', t))
print('NFC', len(u.normalize('NFC', t)))
print('NFKC', len(u.normalize('NFKC', t)), u.normalize('NFC', t) == s[:-1])
