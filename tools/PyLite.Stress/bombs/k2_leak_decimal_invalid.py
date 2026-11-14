# deadline: 3000
# allocmb: 1024
from decimal import Decimal
s = 'x' * 16000000
while True:
    try:
        Decimal(s)
    except Exception:
        pass
