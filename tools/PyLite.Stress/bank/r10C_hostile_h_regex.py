# repeat: 2
# regexms: 50
import re
re.match(r"(a+)+$", "a" * 40 + "b")
