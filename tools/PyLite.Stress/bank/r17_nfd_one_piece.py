# Review 12, row 17 follow-up: a text that is one piece sizes its buffer once a call.
import unicodedata
s = chr(0x439) * 400000
n = 0
for i in range(8):
    n += len(unicodedata.normalize('NFD', s))
print(n)
