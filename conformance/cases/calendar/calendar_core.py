# Adapted from CPython's test suite (Lib/test/test_calendar.py: CalendarTestCase.test_isleap /
# test_leapdays / test_monthrange / test_weekday / test_timegm / test_monthcalendar, OutputTestCase).
# Python Software Foundation, PSF License Version 2 -- see README.
import calendar
from calendar import isleap, leapdays, weekday, monthrange, monthcalendar, timegm

assert isleap(2000) and isleap(2004) and not isleap(1900) and not isleap(2023)
assert leapdays(2010, 2011) == 0 and leapdays(2010, 2012) == 0 and leapdays(2012, 2013) == 1 and leapdays(2000, 2025) == 7
assert weekday(2024, 5, 6) == calendar.MONDAY and weekday(2024, 12, 29) == calendar.SUNDAY
assert monthrange(2024, 2) == (3, 29) and monthrange(2023, 2) == (2, 28) and monthrange(2004, 12) == (2, 31)
# the "last day of the month" idiom
assert [monthrange(2024, m)[1] for m in range(1, 13)] == [31, 29, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31]
assert monthcalendar(2024, 2)[0] == [0, 0, 0, 1, 2, 3, 4] and monthcalendar(2024, 2)[-1] == [26, 27, 28, 29, 0, 0, 0]
assert len(monthcalendar(2024, 9)) == 6 and len(monthcalendar(2021, 2)) == 4      # Feb 2021 began on a Monday: four full weeks
assert timegm((1970, 1, 1, 0, 0, 0, 0, 0, 0)) == 0 and timegm((2024, 1, 1, 0, 0, 0)) == 1704067200
import time
assert timegm(time.gmtime(1700000000)) == 1700000000
assert calendar.month_name[1] == 'January' and calendar.month_name[0] == '' and list(calendar.month_abbr)[1:4] == ['Jan', 'Feb', 'Mar']
assert calendar.day_name[calendar.FRIDAY] == 'Friday' and calendar.day_abbr[calendar.SATURDAY] == 'Sat'
assert calendar.JANUARY == 1 and calendar.DECEMBER == 12 and calendar.firstweekday() == 0
assert calendar.weekheader(2) == 'Mo Tu We Th Fr Sa Su'
assert calendar.month(2004, 12) == (
    '   December 2004\n'
    'Mo Tu We Th Fr Sa Su\n'
    '       1  2  3  4  5\n'
    ' 6  7  8  9 10 11 12\n'
    '13 14 15 16 17 18 19\n'
    '20 21 22 23 24 25 26\n'
    '27 28 29 30 31\n')
try:
    monthrange(2004, 13)
    assert False
except calendar.IllegalMonthError as e:
    assert isinstance(e, ValueError) and str(e) == 'bad month number 13; must be 1-12'

# mdays holds February at 28 (monthrange is what accounts for a leap year); error is the base ValueError
assert calendar.mdays[1] == 31 and calendar.mdays[2] == 28 and calendar.mdays[0] == 0
assert len(calendar.mdays) == 13 and sum(calendar.mdays) == 365
try:
    calendar.monthrange(2026, 13)
    assert False
except calendar.error:
    pass
