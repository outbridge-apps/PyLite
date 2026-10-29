# deadline: 20000
# allocmb: 1024
import csv
print(csv.Sniffer().has_header(('a;b;c' + chr(10)) * 400000))
