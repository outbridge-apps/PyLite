# allocmb: 16
# Review 15: short leading repeats, as dates and codes spell them, are paid what they keep and fit as before.
import re
keep = []
for i in range(3000):
    keep.append(re.compile('0{2}-%d-[0-9]{4}' % i))
print(len(keep), sum(1 for p in keep[:100] if p.search('00-7-2026')))
