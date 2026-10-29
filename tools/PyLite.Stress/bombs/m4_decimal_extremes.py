# deadline: 20000
# allocmb: 1024
from decimal import Decimal, getcontext
r = []
for e in ('1e27', '1e-27', '9.9999999999999999999999999e27', '1e-28'):
    try:
        r.append(str(Decimal(e).sqrt()))
    except Exception as ex:
        r.append(type(ex).__name__)
try:
    r.append(str(Decimal('1e27').exp()))
except Exception as ex:
    r.append(type(ex).__name__)
print(r)
