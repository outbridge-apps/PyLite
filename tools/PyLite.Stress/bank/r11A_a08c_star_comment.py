import re
s = 'ab' * 500000
p = re.compile(r'(?:ab)(?#c)*')
print(p.match(s).end())
