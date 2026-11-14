# deadline: 10000
import re
B = chr(92)
p = '[a-z]' * 800
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a' * 800)), bool(r.search('a' * 800)), r.fullmatch('a' * 800) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
