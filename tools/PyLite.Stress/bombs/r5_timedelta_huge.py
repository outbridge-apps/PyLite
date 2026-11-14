# deadline: 2000
import datetime
for f in (lambda: datetime.timedelta(1) * (2 ** 999999), lambda: datetime.timedelta(days=10 ** 5000)):
    try:
        f()
    except OverflowError as e:
        print(e)
