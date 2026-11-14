# allocmb: 64
# deadline: 20000
import datetime
b = datetime.datetime(2020, 1, 1)
l = [b + datetime.timedelta(seconds=i) for i in range(2000000)]
print(len(l))
