# deadline: 20000
# allocmb: 1024
import re
p = re.compile('(?:a{,3}b){1,4}' * 50000)
print(p is not None)
