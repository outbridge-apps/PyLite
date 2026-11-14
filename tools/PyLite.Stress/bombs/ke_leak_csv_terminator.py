# deadline: 3000
# allocmb: 1024
import csv, io
csv.writer(io.StringIO(), lineterminator='x' * 200000).writerow(['y' * 200000])
