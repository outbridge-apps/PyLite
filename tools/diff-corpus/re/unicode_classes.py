import re

out = []
out.append(re.findall(r'\w+', 'café naïve Привет мир 東京'))
out.append(re.findall(r'\d', '1٢3'))
out.append(re.findall(r'\s', 'a\tb c'))
out.append(re.findall(r'\w+', 'café Привет', re.ASCII))
out.append(re.findall(r'[а-я]+', 'Привет мир'))
out.append(re.findall(r'(?i)привет', 'ПРИВЕТ привет'))
out.append(re.sub(r'\W+', '_', 'naïve façade!'))
out.append(re.match(r'\w+', 'Straße').group())
out.append(re.findall(r'[A-Z]', 'aBcDé'))
result = out
