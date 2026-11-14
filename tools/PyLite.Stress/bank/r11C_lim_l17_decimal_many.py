# allocmb: 64
# deadline: 20000
import decimal
l = [decimal.Decimal(i) / 7 for i in range(2000000)]
print(len(l))
