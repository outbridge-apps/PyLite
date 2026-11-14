# deadline: 3000
# allocmb: 256
import csv, io, itertools
w = csv.DictWriter(io.StringIO(), itertools.repeat('a', 20000000))
