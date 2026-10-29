# deadline: 3000
# allocmb: 1024
import math
import random
random.seed(1)
a = random.getrandbits(999000)
b = random.getrandbits(998000)
x = math.gcd(a, b)
