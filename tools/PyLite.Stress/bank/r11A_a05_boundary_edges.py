import re

subjects = ['a', 'ab', 'a b', 'ba', '_a_', 'a1', 'é a', 'aé', 'éa', 'a' + chr(0x301), chr(0x301) + 'a',
            'K' + chr(0x212A), 'ſs', 'xa ax', 'a-a', '', ' ', 'aaa', 'a\nb', 'ab ba', '1a', 'a' + chr(0x2160),
            chr(0x2160) + 'a', 'a' + chr(0xB2), 'a' + chr(0x5F) + 'b', 'a' + chr(0x203F)]
pats = [r'a\b*', r'a\b+', r'a\b?', r'a\b{2}', r'a\B*', r'\b*a', r'a\b{1,3}b', r'a\b(?#c)*',
        r'a(?#c)\b', r'\b(?#c)a', r'(?x) a \b', r'(?x)a\b', r'(?x)\ba', r'(?x)\ba #c' + '\n' + '*',
        r'(?x)a\b #c' + '\n', r'\ba', r'a\b', r'\Ba', r'a\B', r'\ba\b', r'\Ba\B', r'(?i)\bk', r'(?i)k\b',
        r'(?i)\bs\b', r'(?i)s\B', r'(?<=a\b)', r'(?<=\ba)', r'(?<!a\b) ', r'(?=a\b)', r'(?=\ba)', r'a|\b',
        r'\b|a', r'(a)\b', r'a\b|b', r'_\b', r'\b_', r'1\b', r'\b1', r'a\b\b', r'\b\ba', r'a\b\B', r'\B\ba',
        r'a{1}\b', r'\ba{0}', r'\ba{,1}', r'a\b(?=b)', r'a\Z\b', r'\b$', r'^\b', r'a\b.', r'.\ba', r'[a]\b',
        r'\x61\b', r'\ba?', r'(?i:a)\b', r'(?a)a\b', r'(?u)\bé', r'é\b', r'\bé', r'a\b' + chr(0x301)]
for p in pats:
    row = []
    try:
        rx = re.compile(p)
    except re.error as e:
        print(repr(p), 'error', e)
        continue
    for s in subjects:
        try:
            row.append([m.span() for m in rx.finditer(s)])
            row.append(bool(rx.fullmatch(s)))
            row.append(bool(rx.match(s)))
        except Exception as e:
            row.append(type(e).__name__)
    print(repr(p), row)
