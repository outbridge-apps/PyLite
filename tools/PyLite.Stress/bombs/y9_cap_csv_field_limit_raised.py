# deadline: 3000
# allocmb: 64
import csv
csv.field_size_limit(2147483647)
x = next(csv.reader([chr(34)] + ['x' * 1000000] * 120))
