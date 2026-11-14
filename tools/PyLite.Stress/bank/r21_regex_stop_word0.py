# deadline: 5000
# Review 19: the translator's exact \w is a subtraction class, whose end the walk misread, so \w{0} hid the merged
# loops behind it and no stop stood: on a subject with a word-risk character .NET built a 72,000-character table in
# one call (12 s past a 2 s deadline; with {4000}{4000} the call never returned). The stop stands now.
import re
B = chr(92)
out = []
for p in [B + 'w{0}(?:(?:a{60,}){60,}){20}', B + 'w{0}(?:(?:a{2,}){4000}){4000}']:
    r = re.compile(p)
    out.append((r.match('a' * 10), r.match(chr(0x1885) + 'a' * 10)))
print(out)
