import re

out = []
out.append(re.findall(r'\d+', 'a1 b22 c333'))
out.append(re.findall(r'(\w)=(\d)', 'a=1, b=2, c=x'))
out.append(re.findall(r'(\w)=\d', 'a=1, b=2'))
out.append([m.span() for m in re.finditer(r'o', 'foo boo')])
out.append(re.sub(r'(\w+) (\w+)', r'\2 \1', 'hello world, good day'))
out.append(re.sub(r'(?P<k>\w+)=(?P<v>\w+)', r'\g<v>:\g<k>', 'a=1 b=2'))
out.append(re.sub(r'\d', lambda m: str(int(m.group()) * 2), 'a1b2c9'))
out.append(re.subn(r'x', 'y', 'xaxbx'))
out.append(re.sub(r'x', 'y', 'xaxbx', count=2))
out.append(re.sub(r'', '-', 'abc'))
out.append(re.sub(r'\s+', ' ', '  many   spaces\there \n'))
out.append(re.sub(r'(a)|b', r'[\1]', 'ab'))
out.append(re.compile(r'\d').sub('#', 'a1b2', 1))
out.append(re.findall(r'', 'ab'))
out.append(re.findall(r'a|', 'ab'))
result = out
