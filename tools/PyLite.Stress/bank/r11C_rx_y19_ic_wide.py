# deadline: 10000
import re
B = chr(92)
p = '(?i)[' + chr(0x100) + '-' + chr(0x2FFF) + ']' * 1
try:
    r = re.compile(p)
    print(len(p), r.groups, bool(r.match(chr(0x130))), bool(r.search(chr(0x130))), r.fullmatch(chr(0x130)) is not None)
except re.error as e:
    print("re.error", str(e)[:60])
