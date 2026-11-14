# deadline: 10000
import re
B = chr(92)
p = '(?:a++)++' * 300
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a' * 300)), bool(r.search('a' * 300)), r.fullmatch('a' * 300) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
