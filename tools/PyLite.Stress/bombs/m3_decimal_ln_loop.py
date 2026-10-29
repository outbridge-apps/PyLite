# deadline: 20000
# allocmb: 1024
from decimal import Decimal
s = Decimal(0)
for i in range(1, 200000):
    s += Decimal(i).ln()
print(s > 0)
