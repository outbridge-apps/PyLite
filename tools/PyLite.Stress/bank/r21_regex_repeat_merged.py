# allocmb: 16
# deadline: 60000
# Review 16: .NET merges nested loops of one character into one, (?:a{2,}){1500} a 3,000-character prefix; one past
# the longest literal a pattern can spell is stopped ((?:(?:a{60,}){60,}){20} built 72,000 in one 6 s call).
import re
re.compile('(?:(?:a{60,}){60,}){20}')
keep = []
for i in range(5000):
    keep.append(re.compile('(?:a{2,}){1500}x%d' % i))
print(len(keep))
