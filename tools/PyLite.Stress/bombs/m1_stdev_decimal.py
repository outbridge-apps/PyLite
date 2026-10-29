# deadline: 20000
# allocmb: 1024
import statistics
from decimal import Decimal
print(statistics.stdev([Decimal(i) for i in range(300000)]))
