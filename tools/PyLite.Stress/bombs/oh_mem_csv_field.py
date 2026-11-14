# deadline: 3000
# allocmb: 256
import csv
x = next(csv.reader(['"'] + ['x' * 1000000] * 400))
