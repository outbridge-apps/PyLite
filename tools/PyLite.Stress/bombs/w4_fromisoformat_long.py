# deadline: 20000
# allocmb: 1024
import datetime
try:
    datetime.datetime.fromisoformat('2020-01-01T' + '1' * 2000000)
except ValueError as e:
    print('clean ValueError')
