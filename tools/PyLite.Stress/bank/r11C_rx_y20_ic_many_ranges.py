# deadline: 10000
import re
B = chr(92)
p = '(?i)[' + ''.join(chr(0x100 + 3 * i) + '-' + chr(0x101 + 3 * i) for i in range(1300)) + ']'
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match(chr(0x131))), bool(r.search(chr(0x131))), r.fullmatch(chr(0x131)) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
