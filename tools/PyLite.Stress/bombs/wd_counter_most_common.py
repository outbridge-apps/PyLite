# deadline: 20000
# allocmb: 1024
from collections import Counter
print(Counter('ab' * 2000000).most_common(1))
