# deadline: 10000
import re
B = chr(92)
p = '(?<=a(?<=b(?<=c)))' * 200
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('abc')), bool(r.search('abc')), r.fullmatch('abc') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
