import re
s = chr(0x212a) + chr(0x17f) + "kKsS" + chr(0x131) + chr(0x130)
print([hex(ord(c)) for c in re.findall("(?i)[A-Z]", s)])
print([hex(ord(c)) for c in re.findall("(?i)[J-L]", s)])
print([hex(ord(c)) for c in re.findall("(?i)[K]", s)])
print([hex(ord(c)) for c in re.findall("(?i)[a-z]", s)])
print([hex(ord(c)) for c in re.findall("(?i)[" + chr(92) + "u0041-" + chr(92) + "u005a]", s)])
