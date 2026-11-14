import unicodedata as u
samples = ['ȩ́', 'ḍ̇', 'Ǻ', '각', '각', 'ﬁx', 'q̣̇', 'क़़', 'ab̖́̂̀c', 'a' + '́' * 40 + '̖']
for s in samples:
    print(ascii(s), [ascii(u.normalize(f, s)) for f in ('NFC', 'NFD', 'NFKC', 'NFKD')])
    print([u.is_normalized(f, s) for f in ('NFC', 'NFD', 'NFKC', 'NFKD')])
print(repr(chr(0x1F97A) + chr(0x20BF) + chr(0x378) + chr(0xAD) + chr(0x1E030) + chr(0x1CCF0)))
print([c.isalpha() for c in (chr(0x1E030), chr(0x10D4A), chr(0x1CCF0))], [c.isnumeric() for c in (chr(0x1CCF0), chr(0x10D40), chr(0x16D70))])
