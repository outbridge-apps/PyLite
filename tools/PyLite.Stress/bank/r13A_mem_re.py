import re
B = chr(92)
for unit, label in ((B + 'U0001F600', 'astral'), (B + 'U00000041', 'bmpU'), (B + 'u0041', 'u4'), ('A', 'lit'), (chr(0x1F600), 'rawastral')):
    pats = []
    h0 = heap(); c0 = charged()
    for k in range(20):
        p = re.compile(unit * 400 + str(k))
        pats.append(p)
    h1 = heap(); c1 = charged()
    print(label, 'real/unit', (h1 - h0) // (20 * 400), 'charged/unit', (c1 - c0) // (20 * 400))
    pats = None
