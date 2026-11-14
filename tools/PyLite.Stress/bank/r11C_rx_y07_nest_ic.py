# deadline: 10000
import re
B = chr(92)
p = '(?i:' * 800 + 'k' + ')' * 800
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('K')), bool(r.search('K')), r.fullmatch('K') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
