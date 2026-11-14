# deadline: 10000
import re
B = chr(92)
p = '(?:a|' * 680 + 'b' + ')' * 680
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('b')), bool(r.search('b')), r.fullmatch('b') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
