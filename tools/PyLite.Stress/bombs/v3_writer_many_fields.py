# deadline: 20000
# allocmb: 1024
import csv
from io import StringIO
b = StringIO()
csv.writer(b).writerow(['x'] * 2000000)
print(len(b.getvalue()))
