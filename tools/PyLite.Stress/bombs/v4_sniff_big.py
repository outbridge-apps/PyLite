# deadline: 20000
# allocmb: 1024
import csv
print(csv.Sniffer().sniff('a;b;c' + chr(10)) is not None)
print(csv.Sniffer().sniff(('a;b;c' + chr(10)) * 400000).delimiter)
