# deadline: 60000
# strchars: 1000000
# Review 12, row 13: the limit stands for a form that is itself too long.
import unicodedata as u
s = (chr(0xe9) * 9 + ' ') * 60000
r = u.normalize('NFD', s)
print(len(r))
