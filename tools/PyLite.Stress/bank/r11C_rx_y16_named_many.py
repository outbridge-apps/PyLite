# deadline: 10000
import re
B = chr(92)
p = ''.join('(?P<g%%d>a)' %% i for i in range(400))
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a' * 400)), bool(r.search('a' * 400)), r.fullmatch('a' * 400) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
