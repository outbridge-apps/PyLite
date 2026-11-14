# deadline: 10000
import re
B = chr(92)
p = '(a)?' + B + '1'
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('aa')), bool(r.search('aa')), r.fullmatch('aa') is not None)
except re.error as e:
    print("re.error", str(e)[:60])
