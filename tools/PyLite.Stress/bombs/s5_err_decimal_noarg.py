# deadline: 3000
# allocmb: 256
import decimal
try:
    decimal.Decimal(1).compare()
except TypeError as x:
    print('clean', type(x).__name__)
