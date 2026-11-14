# deadline: 10000
import re
B = chr(92)
p = ('(?:' + B + 'w' + B + 'b)*') * 450
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match('a b c ' * 100)), bool(r.search('a b c ' * 100)), r.fullmatch('a b c ' * 100) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
