# deadline: 3000
# allocmb: 1024
from collections import ChainMap
cm = ChainMap(*([{}] * 4000000))
while 'k' not in cm:
    pass
