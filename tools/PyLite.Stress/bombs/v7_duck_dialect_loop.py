# deadline: 20000
# allocmb: 1024
import csv
class D:
    def __init__(self):
        self.delimiter = ';'
n = 0
for i in range(200000):
    n += len(next(csv.reader(['a;b'], D())))
print(n)
