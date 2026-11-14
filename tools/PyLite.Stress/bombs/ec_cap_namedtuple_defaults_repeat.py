# deadline: 3000
# allocmb: 256
from collections import namedtuple
import itertools
P = namedtuple('P', 'a', defaults=itertools.repeat(0, 20000000))
