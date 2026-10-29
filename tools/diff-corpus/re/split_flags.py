import re

out = []
out.append(re.split(r'[,;]\s*', 'a, b;c,d'))
out.append(re.split(r'([,;])', 'a,b;c'))
out.append(re.split(r',', 'a,b,c,d', maxsplit=2))
out.append(re.findall(r'^\w+', 'one\ntwo\nthree', re.MULTILINE))
out.append(re.findall(r'\w+$', 'one\ntwo\nthree', re.M))
out.append(re.findall(r'^\w+', 'one\ntwo'))
out.append(re.search(r'a.c', 'a\nc') is None)
out.append(re.search(r'a.c', 'a\nc', re.DOTALL).group())
out.append(re.findall(r'abc', 'ABC abc AbC', re.IGNORECASE))
pat = re.compile(r'''
    (\d{3})   # area
    [-.]?
    (\d{4})   # number
''', re.VERBOSE)
out.append(pat.findall('555-1234 555.9876 5551111'))
out.append(re.findall(r'(?i)hello', 'Hello HELLO'))
out.append(bool(re.compile(r'x', re.I | re.M).flags & re.I))
result = out
