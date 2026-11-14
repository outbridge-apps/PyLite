import math
try:
    print(math.comb(2 ** 64, 2 ** 63))
except OverflowError as e:
    print("OE", e)
