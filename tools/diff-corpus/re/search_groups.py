import re

out = []
m = re.search(r'(?P<user>\w+)@(?P<host>[\w.]+)', 'mail: anna.k@mail.example.org, x')
out.append((m.group(), m.group(1), m.group('host'), m.groups(), sorted(m.groupdict().items())))
out.append((m.start(), m.end(), m.span(), m.span('user')))
out.append(re.match(r'\d+', 'abc') is None)
out.append(re.match(r'a(b)?c', 'ac').groups())
out.append(re.match(r'a(b)?c', 'ac').groups('-'))
out.append(re.fullmatch(r'[a-z]+', 'abc') is not None)
out.append(re.fullmatch(r'[a-z]+', 'abc1') is None)
p = re.compile(r'(\d{4})-(\d{2})-(\d{2})')
m = p.search('due 2026-10-29 and 2027-01-01')
out.append((m.group(1, 3), m.lastindex, p.groups, p.pattern))
out.append([mm.group(2) for mm in p.finditer('2026-10-29 2027-01-01')])
m = re.search(r'(a)(b)?', 'a')
out.append((m.group(2), m.span(2)))
m = re.search(r'(?P<x>a)|(?P<y>b)', 'b')
out.append((m.lastgroup, m.group('x'), m.group('y')))
out.append(re.compile(r'b').search('abcb', 2).start())
out.append(re.compile(r'^b').search('ab', 1))
result = out
