# repeat: 2
# deadline: 60000
import re, time
T = time.perf_counter
out = []
for label, fmt in (("lit", "name%d"), ("cls", "[a-z]+%d\\s*=\\s*(\\d+)"), ("w", "\\w+%d"), ("b", "\\bn%d\\b"), ("ic", "(?i)name%d")):
    re.purge()
    t = T()
    for i in range(1500):
        re.compile(fmt % i)
    out.append("%s=%d" % (label, (T() - t) * 1000))
print(" ".join(out))
