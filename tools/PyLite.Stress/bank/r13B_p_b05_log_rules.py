# deadline: 60000
# a log of 60,000 lines through 12 extraction rules, groupdict, sub with a callback, split
import re
import time
levels = ['INFO', 'WARN', 'ERROR', 'DEBUG']
lines = []
for i in range(60000):
    lines.append('2026-11-%02d 12:%02d:%02d.%03d [%s] svc%d user=u%d ip=10.0.%d.%d took=%dms msg="order %d %s"' % (
        i % 28 + 1, i % 60, (i * 7) % 60, i % 1000, levels[i % 4], i % 9, i % 500, i % 256, (i * 3) % 256, i % 900, i, 'ok' if i % 5 else 'failed'))
log = '\n'.join(lines)
rules = [
    r'(?P<date>\d{4}-\d\d-\d\d) (?P<time>[\d:.]+) \[(?P<lvl>\w+)\] (?P<svc>\w+)',
    r'user=(?P<user>\w+)',
    r'ip=(?P<ip>(?:\d{1,3}\.){3}\d{1,3})',
    r'took=(?P<ms>\d+)ms',
    r'msg="(?P<msg>[^"]*)"',
    r'\border (\d+)\b',
    r'(?i)\bfailed\b',
    r'\[(ERROR|WARN)\]',
    r'(?m)^2026-11-0[1-9]',
    r'svc[0-4]\b',
    r'(\d+)\.(\d+)\.(\d+)\.(\d+)',
    r'(?:[A-Za-z]+ ){1,3}\d+',
]
t0 = time.perf_counter()
pats = [re.compile(r) for r in rules]
counts = []
for p in pats:
    c = 0
    for m in p.finditer(log):
        c += 1
        m.group(0)
    counts.append(c)
t1 = time.perf_counter()
slow = 0
users = {}
p0 = pats[0]
for ln in lines:
    m = p0.match(ln)
    d = m.groupdict()
    u = pats[1].search(ln).group('user')
    users[u] = users.get(u, 0) + 1
    if int(pats[3].search(ln).group('ms')) > 800:
        slow += 1
t2 = time.perf_counter()
masked = pats[2].sub(lambda m: m.group(0).rsplit('.', 1)[0] + '.x', log)
parts = re.split(r'\s+', lines[0])
words = len(re.findall(r'\w+', log))
t3 = time.perf_counter()
print(counts, slow, len(users), len(masked), len(parts), words, 'find', round(t1 - t0, 3), 'lines', round(t2 - t1, 3), 'sub', round(t3 - t2, 3))
