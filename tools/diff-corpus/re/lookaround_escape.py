import re

out = []
out.append(re.escape('a.b*c'))
out.append(re.findall(r'\w+(?=,)', 'a, b, c'))
out.append(re.findall(r'(?<=\$)\d+', 'cost $15 and $200'))
out.append(re.findall(r'\b\w+(?!\d)\b', 'ab1 cd ef2'))
out.append(re.findall(r'(?<!-)\b\d+', '-5 6 -7 8'))
out.append(re.findall(r'<.+?>', '<a><b></b></a>'))
out.append(re.findall(r'<.+>', '<a><b></b></a>'))
out.append(re.search(r'(\w)\1', 'abccd').group())
out.append(re.findall(r'(\w)(\w)\2\1', 'abba xyyx abab'))
out.append(re.findall(r'cat|dog|bird', 'dog cat fish bird'))
out.append(re.findall(r'(?:ab)+', 'ababab ab abb'))
out.append(re.findall(r'a{2,3}', 'a aa aaa aaaa'))
out.append(re.findall(r'[^aeiou\s]+', 'hello world'))
out.append(re.match(r'(?P<q>["\']).*?(?P=q)', '"quoted" rest').group())
out.append(re.findall(r'\bcat\b', 'cat concat cat.'))
out.append(re.findall(r'\Bcat', 'cat concat'))
result = out
