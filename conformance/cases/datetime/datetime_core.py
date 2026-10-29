# Adapted from CPython's test suite (Lib/test/datetimetester.py basics).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Deliberately avoids now()/today() (wall-clock dependent).

from datetime import date, datetime, timedelta

# date construction and fields
d = date(2024, 2, 29)                       # a leap day constructs fine
assert d.year == 2024 and d.month == 2 and d.day == 29
raised = False
try:
    date(2023, 2, 29)                        # not a leap year
except ValueError:
    raised = True
assert raised

# weekday: Monday=0; isoweekday: Monday=1
assert date(2024, 2, 29).weekday() == 3      # Thursday
assert date(2024, 2, 29).isoweekday() == 4
assert date(2024, 1, 1).weekday() == 0       # 2024-01-01 was a Monday

# isoformat
assert date(2024, 3, 5).isoformat() == '2024-03-05'
assert str(date(2024, 3, 5)) == '2024-03-05'

# date arithmetic with timedelta
assert date(2024, 1, 31) + timedelta(days=1) == date(2024, 2, 1)
assert date(2024, 3, 1) - timedelta(days=1) == date(2024, 2, 29)
assert (date(2024, 3, 1) - date(2024, 2, 1)).days == 29
assert date(2023, 3, 1) - timedelta(days=1) == date(2023, 2, 28)

# comparisons
assert date(2024, 1, 1) < date(2024, 1, 2)
assert date(2024, 1, 1) == date(2024, 1, 1)

# datetime fields and isoformat
dt = datetime(2024, 3, 5, 14, 30, 15)
assert dt.hour == 14 and dt.minute == 30 and dt.second == 15
assert dt.isoformat() == '2024-03-05T14:30:15'
assert dt.date() == date(2024, 3, 5)

# datetime arithmetic crosses midnight correctly
assert datetime(2024, 1, 1, 23, 30) + timedelta(hours=1) == datetime(2024, 1, 2, 0, 30)
delta = datetime(2024, 1, 2, 1, 0) - datetime(2024, 1, 1, 23, 0)
assert delta == timedelta(hours=2)

# timedelta normalization and total_seconds
td = timedelta(days=1, hours=12)
assert td.total_seconds() == 129600.0
assert timedelta(hours=25) == timedelta(days=1, hours=1)
assert timedelta(seconds=3661) == timedelta(hours=1, minutes=1, seconds=1)
assert timedelta(days=1) > timedelta(hours=23)
assert timedelta(minutes=1) * 3 == timedelta(minutes=3)
assert timedelta(hours=1) / 2 == timedelta(minutes=30)
assert -timedelta(days=1) == timedelta(days=-1)

# strftime
assert date(2024, 3, 5).strftime('%Y-%m-%d') == '2024-03-05'
assert datetime(2024, 3, 5, 9, 7, 3).strftime('%H:%M:%S') == '09:07:03'
assert date(2024, 3, 5).strftime('%d/%m/%Y') == '05/03/2024'

# strptime
parsed = datetime.strptime('2024-03-05 14:30', '%Y-%m-%d %H:%M')
assert parsed == datetime(2024, 3, 5, 14, 30)

# fromordinal / toordinal round trip
assert date.fromordinal(1) == date(1, 1, 1)
assert date.fromordinal(date(2024, 3, 5).toordinal()) == date(2024, 3, 5)
