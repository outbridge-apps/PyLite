import re
s = 'ab' * 500000
p = re.compile(r'(?:(?:(?:ab)(?#c){1,64})(?#c){1,64})(?#c){1,64}')
print(p.match(s).end())
