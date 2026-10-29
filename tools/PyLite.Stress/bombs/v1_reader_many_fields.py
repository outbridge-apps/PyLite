# deadline: 20000
# allocmb: 1024
import csv
print(len(next(csv.reader(['a,' * 2000000 + 'a']))))
