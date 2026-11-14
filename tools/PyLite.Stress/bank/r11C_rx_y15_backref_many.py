# deadline: 10000
import re
B = chr(92)
p = '(a)' + (B + '1') * 2000
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a' * 2001)), bool(r.search('a' * 2001)), r.fullmatch('a' * 2001) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
