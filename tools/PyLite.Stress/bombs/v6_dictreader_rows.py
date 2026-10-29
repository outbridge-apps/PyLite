# deadline: 20000
# allocmb: 1024
import csv
rows = ['a,b'] + ['1,2'] * 500000
print(sum(1 for _ in csv.DictReader(rows)))
