# deadline: 3000
# allocmb: 256
import csv, io, itertools
csv.writer(io.StringIO()).writerow(itertools.repeat(None, 20000000))
