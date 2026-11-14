import re
subj = "ab cd\nef_gh ij\n\nxyz"
pats = [r"\b\w+\b", r"\w+$", r"(?m)\w+$", r"\Z", r"$", r"(?=\w)", r"(?<=\w)\b", r"\B", r"\w*", r"(?!x)\w", r"\w+(?=\s)",
        r"(?s).+", r".*", r"^\w+", r"(?m)^\w*", r"\s*$", r"(\w)(?=(\w))", r"(?i)[A-Z]+\b", r"(?<!\w)\w{2}(?!\w)", r"", r"\b",
        r"x?$", r"(?:\w\w)*", r"\w{3,}", r"(a|b|\s)+"]
out = []
for p in pats:
    r = re.compile(p)
    for pos in (0, 1, 2, 3, 5, 6, 9, 15):
        for endpos in (0, 1, 2, 4, 5, 6, 8, 9, 14, 15, 16, len(subj), 100):
            if pos > endpos:
                continue
            a = r.findall(subj, pos, endpos)
            b = [(m.span(), m.group()) for m in r.finditer(subj, pos, endpos)]
            s = r.search(subj, pos, endpos)
            mm = r.match(subj, pos, endpos)
            fm = r.fullmatch(subj, pos, endpos)
            out.append("%r %d %d %r %r %r %r %r" % (p, pos, endpos, a, b, s and (s.span(), s.groups(), s.endpos, s.pos, s.string == subj),
                                                     mm and mm.span(), fm and fm.span()))
# astral + surrogate + word-risk subjects
for subj2 in ["İxſ k", "a\U0001F600b c", "KKk d", "xͅy z"]:
    for p in [r"\b\w+\b", r"(?i)k+", r"\w+$", r".\b"]:
        r = re.compile(p)
        for endpos in range(len(subj2) + 1):
            out.append("%r %r %d %r %r" % (subj2, p, endpos, r.findall(subj2, 0, endpos), [m.span() for m in r.finditer(subj2, 1, endpos)] if endpos >= 1 else None))
for line in out:
    print(line)
print(len(out))
