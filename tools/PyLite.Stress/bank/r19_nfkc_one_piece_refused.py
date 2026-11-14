# allocmb: 1024
# deadline: 30000
# Review 13, row 28: NFKC of 900,000 U+FDFA is 16.2M chars, past the limit: refused before it is composed.
import unicodedata as u
s = "ﷺ" * 900000
try:
    r = u.normalize("NFKC", s)
    print(len(r))
except MemoryError as e:
    print("MemoryError", e)
