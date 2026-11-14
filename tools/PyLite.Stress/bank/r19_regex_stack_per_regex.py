# Review 13, row 25: six kept rules with a group loop, each used by search, match and fullmatch over 500K chars.
# Each of the three regexes keeps a stack as long as the subject: three are charged now, not one.
import re
s = "ab" * 250000
keep = []
for i in range(6):
    c = re.compile(r"(?:ab|x%d)*$" % i)
    c.search(s); c.match(s); c.fullmatch(s)
    keep.append(c)
print(len(keep))
