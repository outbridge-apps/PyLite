# deadline: 20000
# allocmb: 1024
import datetime
try:
    datetime.datetime.strptime('x' * 2000000, '%Y')
except ValueError as e:
    print('clean ValueError')
