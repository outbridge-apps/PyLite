import re
cps = [0x85, 0xA0, 0xAD, 0xB5, 0xDF, 0x130, 0x131, 0x17F, 0x1C5, 0x2028, 0x2029, 0x200B, 0x200D, 0xFEFF, 0x3000,
       0x0301, 0x0345, 0x03C2, 0x1E9E, 0x212A, 0x212B, 0xFB00, 0xFF3F, 0x2160, 0x24B6, 0x10400 >> 0, 0xD800, 0xDFFF,
       0x5D, 0x5C, 0x2D, 0x5E, 0x23, 0x20]
subject = ''.join(chr(c) for c in cps if c < 0x10000) + 'aAzZ09_-]^\\#'
out = []
for flags in (0, re.I, re.I | re.X, re.X):
    for c in cps:
        if c >= 0x10000:
            continue
        ch = chr(c)
        for tmpl in ('[%s]', '[^%s]', '[a%s]', '[%s-%s]', '[\\w%s]', '[%s\\d]', '[\\W%s]'):
            body = tmpl % ((re.escape(ch),) * tmpl.count('%s'))
            try:
                rx = re.compile(body, flags)
                out.append(len(rx.findall(subject)))
            except re.error as e:
                out.append('E')
    for p in (r'\w+', r'\W+', r'\b\w', r'\B.', r'[\w-]+', r'(?i)[а-яё]+', r'(?i)[^\W\d_]+'):
        try:
            out.append([m.span() for m in re.finditer(p, subject + ' Ёлка ǅ ß ẞ ſ K', flags)])
        except re.error:
            out.append('E')
print(out)
