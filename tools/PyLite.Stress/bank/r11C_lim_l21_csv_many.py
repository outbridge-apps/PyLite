# allocmb: 64
# deadline: 20000
import csv, io
b = io.StringIO()
w = csv.writer(b)
for i in range(400000):
    w.writerow([i, "a,b", "x\"y"])
r = list(csv.reader(io.StringIO(b.getvalue())))
print(len(r))
