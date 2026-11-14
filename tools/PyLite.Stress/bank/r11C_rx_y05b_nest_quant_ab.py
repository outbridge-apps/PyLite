# deadline: 10000
import re
B = chr(92)
p = '(?:' * 800 + 'ab' + ')*' * 800
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('ab' * 50 + 'c')), bool(r.search('ab' * 50 + 'c')), r.fullmatch('ab' * 50 + 'c') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
