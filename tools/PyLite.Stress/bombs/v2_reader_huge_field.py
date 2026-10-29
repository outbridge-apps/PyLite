# deadline: 20000
# allocmb: 1024
import csv
csv.field_size_limit(2000000000)
print(len(next(csv.reader(['"' + 'x' * 8000000 + '"']))[0]))
