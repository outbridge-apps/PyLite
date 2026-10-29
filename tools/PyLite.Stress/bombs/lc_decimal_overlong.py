# deadline: 20000
# allocmb: 1024
from decimal import Decimal
try:
    Decimal('9' * 1000000)
except ArithmeticError as e:
    print('clean ' + type(e).__name__)
