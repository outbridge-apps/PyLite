# deadline: 60000
import re, time
T = time.perf_counter
words = ["name%d" % i for i in range(300)]
plain = " ".join(words) + " caf" + chr(0xe9) + " na" + chr(0xef) + "ve"
risky = plain + " m" + chr(0xb2)          # SUPERSCRIPT TWO: \w differs between sre and .NET
comb = plain + " e" + chr(0x301)          # combining acute
out = []
for label, text in (("plain", plain), ("sup2", risky), ("comb", comb)):
    re.purge()
    t = T()
    n = 0
    for w in words:
        if re.search(r"\b" + re.escape(w) + r"\b", text):
            n += 1
    out.append("%s=%d(%d)" % (label, (T() - t) * 1000, n))
print(" ".join(out))
