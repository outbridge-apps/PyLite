# repeat: 2
import re
for i in range(600):
    re.compile("a%d(b)" % i)
