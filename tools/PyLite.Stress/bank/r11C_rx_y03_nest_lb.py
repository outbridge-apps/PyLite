# deadline: 10000
import re
B = chr(92)
p = '(?<=' * 800 + 'a' + ')' * 800
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a')), bool(r.search('a')), r.fullmatch('a') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
