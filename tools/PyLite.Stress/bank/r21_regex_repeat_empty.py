# allocmb: 64
# deadline: 60000
# Review 16: an empty group after the loop ((?i:), (?:(?:)), (?:){1}) is reduced away and the loops still merge:
# (?:a{10,}(?i:)){400} keeps 24.5 KB a regex and was charged 1.8 KB. 500 kept are charged what they keep now.
import re
keep = []
forms = ['(?:a{10,}(?i:)){400}x%d', '(?:a{10,}(?:(?:))){400}x%d', '(?:a{10,}(?:){1}){400}x%d']
for i in range(500):
    keep.append(re.compile(forms[i % len(forms)] % i))
print(len(keep))
