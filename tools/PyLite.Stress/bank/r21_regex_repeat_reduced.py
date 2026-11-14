# allocmb: 16
# deadline: 60000
# Review 16: .NET reduces a non-capturing group of one loop to the loop and drops empty items before merging nested
# loops, and makes [aa] the character a: each form is held, and one past the longest literal is stopped (each of
# the first three built a 72,000-character table in one 6 s call).
import re
for p in ['(?:(?:(?:a{60,})){60,}){20}', '(?:(?:a{60,}(?:)){60,}){20}', '(?:(?:[aa]{60,}){60,}){20}']:
    re.compile(p)
keep = []
forms = ['(?:(?:a{10,})){300}x%d', '(?:a{10,}(?:)){300}x%d', '[aa]{3000}x%d', '[a-a]{3000}x%d']
for i in range(4000):
    keep.append(re.compile(forms[i % len(forms)] % i))
print(len(keep))
