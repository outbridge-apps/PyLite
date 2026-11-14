# deadline: 3000
# allocmb: 256
import re
p = re.compile('()' * 2000)
kept = list(p.finditer('a' * 4000))
