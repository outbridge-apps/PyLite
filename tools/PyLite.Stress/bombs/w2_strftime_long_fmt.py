# deadline: 20000
# allocmb: 1024
import datetime
print(len(datetime.datetime(2020, 1, 1).strftime('%Y-' * 1000000)))
