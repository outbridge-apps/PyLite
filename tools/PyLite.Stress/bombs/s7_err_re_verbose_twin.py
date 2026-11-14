# deadline: 3000
# allocmb: 256
import re
print(bool(re.fullmatch('(?x)a # trailing comment', 'a')))
