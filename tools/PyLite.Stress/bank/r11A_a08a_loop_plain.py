import re
s = 'ab' * 500000
p = re.compile(r'(?:(?:(?:ab){1,64}){1,64}){1,64}')
print(p.match(s).end())
