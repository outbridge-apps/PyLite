# allocmb: 16
# deadline: 60000
# Review 16: .NET drops an atom or group quantified {0}, so the loop behind it is the prefix: b{0} in front hid it from
# both the charge and the stop ((?:(?:a{2,}){4000}){4000} behind it hung the host). Each form is held or stopped now.
import re
re.compile('b{0}(?:(?:a{60,}){60,}){20}')
re.compile('b{0}(?:(?:a{2,}){4000}){4000}')
keep = []
forms = ['b{0}a{1500}x%d', '(xy){0}a{1500}x%d', '(?:a{10,}x{0}){150}x%d']
for i in range(4000):
    keep.append(re.compile(forms[i % len(forms)] % i))
print(len(keep))
