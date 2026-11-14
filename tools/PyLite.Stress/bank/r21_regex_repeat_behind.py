# allocmb: 16
# deadline: 60000
# Review 15: the same prefix behind what .NET's prefix search passes (a lookahead, options, a named group, a class of
# one): each is held as a leading a{1500} is.
import re
keep = []
forms = ['(?=a)a{1500}x%d', '(?i)a{1500}x%d', '(?P<g>a{1500})x%d', '[a]{1500}x%d', '^(?:a{1500,})x%d']
for i in range(5000):
    keep.append(re.compile(forms[i % len(forms)] % i))
print(len(keep))
