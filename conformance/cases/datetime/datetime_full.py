# Adapted from CPython's test suite (Lib/test/datetimetester.py: TestDate, TestDateTime, TestTime,
# TestTimeDelta, TestTimeZone -- constructors, arithmetic, comparisons, replace/isoformat/strftime/
# ordinal/isocalendar/timestamp/combine/strptime, bounds and error types).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every datetime.* member the dialect exposes is exercised here.

import datetime
from datetime import date, time, datetime as dt, timedelta, timezone, MINYEAR, MAXYEAR

assert MINYEAR == 1 and MAXYEAR == 9999

# ================= timedelta =================
td = timedelta(days=1, hours=2, minutes=3, seconds=4, milliseconds=5, microseconds=6)
assert (td.days, td.seconds, td.microseconds) == (1, 7384, 5006)       # normalised to days/seconds/us
assert timedelta(hours=25) == timedelta(days=1, seconds=3600) and timedelta(seconds=-1).days == -1
assert timedelta(seconds=-1).seconds == 86399 and timedelta(weeks=1) == timedelta(days=7)
assert timedelta(days=0.5) == timedelta(hours=12) and timedelta(seconds=1.5) == timedelta(seconds=1, milliseconds=500)
assert timedelta(microseconds=-1) == timedelta(days=-1, seconds=86399, microseconds=999999)
assert timedelta() == timedelta(0) and not timedelta() and timedelta(microseconds=1)
assert timedelta(1, 2, 3) == timedelta(days=1, seconds=2, microseconds=3)
assert td.total_seconds() == 93784.005006 and timedelta(days=1).total_seconds() == 86400.0
assert timedelta(minutes=1).total_seconds() == 60.0 and timedelta(microseconds=1).total_seconds() == 1e-06

# arithmetic
a = timedelta(days=1)
h = timedelta(hours=1)
assert a + h == timedelta(hours=25) and a - h == timedelta(hours=23) and h - a == timedelta(hours=-23)
assert a * 2 == timedelta(days=2) and 2 * a == timedelta(days=2) and a * 0.5 == timedelta(hours=12)
assert a / 2 == timedelta(hours=12) and a / timedelta(hours=12) == 2.0 and a // 2 == timedelta(hours=12)
assert a // timedelta(hours=7) == 3 and a % timedelta(hours=7) == timedelta(hours=3)
assert divmod(a, timedelta(hours=7)) == (3, timedelta(hours=3))
assert -a == timedelta(days=-1) and +a == a and abs(timedelta(days=-1)) == timedelta(days=1)
assert a / 1.5 == timedelta(hours=16) and timedelta(seconds=10) / 3 == timedelta(seconds=3, microseconds=333333)
assert timedelta(microseconds=3) // 2 == timedelta(microseconds=1) and timedelta(microseconds=-3) // 2 == timedelta(microseconds=-2)
assert sum([timedelta(1), timedelta(2)], timedelta()) == timedelta(3)
try:
    a + 1
    assert False
except TypeError:
    pass
try:
    a / timedelta()
    assert False
except ZeroDivisionError:
    pass
try:
    a * a
    assert False
except TypeError:
    pass

# comparisons, hashing, str/repr
assert timedelta(1) < timedelta(2) and timedelta(1) <= timedelta(1) and timedelta(2) > timedelta(1) and timedelta(1) != timedelta(2)
assert timedelta(1) == timedelta(hours=24) and hash(timedelta(1)) == hash(timedelta(hours=24))
assert sorted([timedelta(3), timedelta(1), timedelta(2)]) == [timedelta(1), timedelta(2), timedelta(3)]
assert max(timedelta(1), timedelta(2)) == timedelta(2) and timedelta(1) in {timedelta(hours=24)}
assert timedelta(1) != 1 and not (timedelta(1) == 86400)
try:
    timedelta(1) < 1
    assert False
except TypeError:
    pass
assert str(timedelta(days=1, hours=2, minutes=3)) == '1 day, 2:03:00'
assert str(timedelta(days=2, seconds=5)) == '2 days, 0:00:05' and str(timedelta(seconds=-1)) == '-1 day, 23:59:59'
assert str(timedelta(hours=1, microseconds=5)) == '1:00:00.000005' and str(timedelta()) == '0:00:00'
assert str(timedelta(minutes=90)) == '1:30:00' and str(timedelta(days=-2)) == '-2 days, 0:00:00'
assert repr(timedelta(days=1, seconds=1)) == 'datetime.timedelta(days=1, seconds=1)'
assert repr(timedelta()) == 'datetime.timedelta(0)' and repr(timedelta(microseconds=3)) == 'datetime.timedelta(microseconds=3)'
assert repr(timedelta(days=-1)) == 'datetime.timedelta(days=-1)'
assert timedelta.min.days == -999999999 and timedelta.max.days == 999999999 and timedelta.resolution == timedelta(microseconds=1)
try:
    timedelta(days=1000000000)
    assert False
except OverflowError:
    pass
try:
    timedelta(seconds='1')
    assert False
except TypeError:
    pass

# ================= date =================
d = date(2024, 2, 29)
assert (d.year, d.month, d.day) == (2024, 2, 29)
assert date(2024, 1, 1).weekday() == 0 and date(2024, 1, 1).isoweekday() == 1        # Monday
assert date(2000, 1, 1).weekday() == 5 and date(1970, 1, 1).weekday() == 3
assert d.isocalendar() == (2024, 9, 4) and date(2021, 1, 3).isocalendar() == (2020, 53, 7)
assert date(2024, 12, 30).isocalendar() == (2025, 1, 1)
assert d.isoformat() == '2024-02-29' and str(d) == '2024-02-29' and repr(d) == 'datetime.date(2024, 2, 29)'
assert d.ctime() == 'Thu Feb 29 00:00:00 2024'
assert date(1, 1, 1).toordinal() == 1 and date(2024, 2, 29).toordinal() == 738945 and date.fromordinal(738945) == d
assert date.fromordinal(1) == date(1, 1, 1) and date.fromordinal(date(9999, 12, 31).toordinal()) == date(9999, 12, 31)
assert d.replace(year=2023, day=28) == date(2023, 2, 28) and d.replace() == d and d.replace(month=3) == date(2024, 3, 29)
assert d.timetuple()[:6] == (2024, 2, 29, 0, 0, 0) and d.timetuple().tm_yday == 60 and d.timetuple().tm_wday == 3
assert d.timetuple().tm_isdst == -1
assert date.min == date(1, 1, 1) and date.max == date(9999, 12, 31) and date.resolution == timedelta(days=1)
assert isinstance(date.today(), date) and date.today().year >= 2024
assert date.fromtimestamp(0).year in (1969, 1970)

# strftime: every supported directive
assert d.strftime('%Y-%m-%d') == '2024-02-29' and d.strftime('%y') == '24' and d.strftime('%j') == '060'
assert d.strftime('%a %A %b %B') == 'Thu Thursday Feb February' and d.strftime('%w') == '4'
assert d.strftime('%H:%M:%S %I %p %f') == '00:00:00 12 AM 000000' and d.strftime('%%') == '%'
assert d.strftime('%c') == 'Thu Feb 29 00:00:00 2024' and d.strftime('%x') == '02/29/24' and d.strftime('%X') == '00:00:00'
assert date(2024, 1, 1).strftime('%U') == '00' and date(2024, 1, 1).strftime('%W') == '01'
assert date(2024, 1, 7).strftime('%U') == '01' and date(2024, 12, 31).strftime('%j') == '366'
assert d.strftime('%z') == '' and d.strftime('%Z') == '' and d.strftime('literal') == 'literal'
assert date(2024, 7, 4).strftime('%d/%m/%Y') == '04/07/2024'

# arithmetic and comparison
assert d + timedelta(days=1) == date(2024, 3, 1) and d - timedelta(days=60) == date(2023, 12, 31)
assert timedelta(days=1) + d == date(2024, 3, 1)
assert date(2024, 3, 1) - d == timedelta(days=1) and (d - date(2023, 2, 28)).days == 366
assert d < date(2024, 3, 1) and d <= d and date(2025, 1, 1) > d and d != date(2024, 3, 1) and d == date(2024, 2, 29)
assert sorted([date(2024, 3, 1), d]) == [d, date(2024, 3, 1)] and hash(d) == hash(date(2024, 2, 29)) and d in {date(2024, 2, 29)}
assert min(d, date(2020, 1, 1)) == date(2020, 1, 1)
try:
    d < dt(2024, 2, 29)
    assert False
except TypeError:
    pass
try:
    d + 1
    assert False
except TypeError:
    pass
try:
    d - dt(2024, 1, 1)
    assert False
except TypeError:
    pass

# constructor errors
for bad in ((2023, 2, 29), (2024, 13, 1), (2024, 0, 1), (2024, 4, 31), (0, 1, 1), (10000, 1, 1), (2024, 1, 0)):
    try:
        date(*bad)
        assert False, bad
    except ValueError:
        pass
try:
    date(2024, 1.5, 1)
    assert False
except TypeError:
    pass
try:
    date.fromordinal(0)
    assert False
except ValueError:
    pass
try:
    d.replace(month=2, day=30)
    assert False
except ValueError:
    pass
try:
    date.max + timedelta(days=1)
    assert False
except OverflowError:
    pass

# ================= time =================
t = time(13, 45, 30, 123456)
assert (t.hour, t.minute, t.second, t.microsecond) == (13, 45, 30, 123456) and t.tzinfo is None
assert time() == time(0, 0) and time(5) == time(5, 0, 0, 0) and time(hour=1, minute=2) == time(1, 2)
assert t.isoformat() == '13:45:30.123456' and time(1, 2).isoformat() == '01:02:00' and str(time(1, 2, 3)) == '01:02:03'
assert t.isoformat('hours') == '13' and t.isoformat(timespec='minutes') == '13:45' and t.isoformat('seconds') == '13:45:30'
assert t.isoformat('milliseconds') == '13:45:30.123' and t.isoformat('microseconds') == '13:45:30.123456'
assert time(1, 2).isoformat('auto') == '01:02:00'
assert repr(t) == 'datetime.time(13, 45, 30, 123456)' and repr(time(1)) == 'datetime.time(1, 0)'
assert t.strftime('%H:%M:%S.%f %I %p') == '13:45:30.123456 01 PM' and time(0, 5).strftime('%I %p') == '12 AM'
assert t.replace(hour=0) == time(0, 45, 30, 123456) and t.replace(microsecond=0).isoformat() == '13:45:30'
assert t.utcoffset() is None and t.dst() is None and t.tzname() is None
assert time(1) < time(2) and time(1, 1) > time(1, 0) and time(1) == time(1, 0) and hash(time(1)) == hash(time(1, 0))
assert time.min == time(0, 0) and time.max == time(23, 59, 59, 999999) and time.resolution == timedelta(microseconds=1)
tz = time(1, 2, tzinfo=timezone.utc)
assert tz.tzinfo is timezone.utc and tz.utcoffset() == timedelta(0) and tz.tzname() == 'UTC' and tz.isoformat() == '01:02:00+00:00'
assert time(1, 2, tzinfo=timezone(timedelta(hours=2))).isoformat() == '01:02:00+02:00'
assert time(1, 2, tzinfo=timezone(timedelta(hours=2))).strftime('%z %Z') == '+0200 UTC+02:00'
assert bool(time(0, 0)) and bool(t)
for bad in ((24, 0), (0, 60), (0, 0, 60), (0, 0, 0, 1000000), (-1, 0)):
    try:
        time(*bad)
        assert False, bad
    except ValueError:
        pass
try:
    time(1) < time(1, tzinfo=timezone.utc)
    assert False
except TypeError:
    pass
try:
    t + timedelta(hours=1)
    assert False
except TypeError:
    pass

# ================= datetime =================
n = dt(2024, 5, 6, 7, 8, 9, 10)
assert (n.year, n.month, n.day, n.hour, n.minute, n.second, n.microsecond) == (2024, 5, 6, 7, 8, 9, 10) and n.tzinfo is None
assert dt(2024, 5, 6) == dt(2024, 5, 6, 0, 0) and dt(2024, 5, 6, hour=1) == dt(2024, 5, 6, 1)
assert n.date() == date(2024, 5, 6) and n.time() == time(7, 8, 9, 10) and n.timetz() == time(7, 8, 9, 10)
assert dt.combine(date(2024, 5, 6), time(7, 8, 9, 10)) == n and dt.combine(date(2024, 5, 6), time(1, 2, tzinfo=timezone.utc)).tzinfo is timezone.utc
assert n.isoformat() == '2024-05-06T07:08:09.000010' and dt(2024, 5, 6, 7, 8).isoformat() == '2024-05-06T07:08:00'
assert n.isoformat(' ') == '2024-05-06 07:08:09.000010' and n.isoformat(sep='T', timespec='minutes') == '2024-05-06T07:08'
assert n.isoformat(timespec='seconds') == '2024-05-06T07:08:09' and n.isoformat(timespec='milliseconds') == '2024-05-06T07:08:09.000'
assert str(n) == '2024-05-06 07:08:09.000010' and str(dt(2024, 5, 6, 7, 8)) == '2024-05-06 07:08:00'
assert repr(n) == 'datetime.datetime(2024, 5, 6, 7, 8, 9, 10)' and repr(dt(2024, 5, 6)) == 'datetime.datetime(2024, 5, 6, 0, 0)'
assert n.ctime() == 'Mon May  6 07:08:09 2024' and n.weekday() == 0 and n.isoweekday() == 1 and n.isocalendar() == (2024, 19, 1)
assert n.toordinal() == date(2024, 5, 6).toordinal() and n.timetuple()[:6] == (2024, 5, 6, 7, 8, 9) and n.timetuple().tm_yday == 127
assert n.utctimetuple()[:6] == (2024, 5, 6, 7, 8, 9) and n.utctimetuple().tm_isdst == 0
assert n.replace(year=2025, microsecond=0) == dt(2025, 5, 6, 7, 8, 9) and n.replace(tzinfo=timezone.utc).tzinfo is timezone.utc
assert n.strftime('%Y-%m-%d %H:%M:%S.%f') == '2024-05-06 07:08:09.000010' and n.strftime('%I:%M %p') == '07:08 AM'
assert dt(2024, 5, 6, 13).strftime('%I %p %H') == '01 PM 13' and dt(2024, 5, 6, 0).strftime('%I') == '12'
assert n.strftime('%a %d %b %Y') == 'Mon 06 May 2024' and n.strftime('%j %U %W %w') == '127 18 19 1'
assert n.utcoffset() is None and n.dst() is None and n.tzname() is None
assert dt.min == dt(1, 1, 1) and dt.max == dt(9999, 12, 31, 23, 59, 59, 999999) and dt.resolution == timedelta(microseconds=1)
assert isinstance(dt.now(), dt) and isinstance(dt.utcnow(), dt) and isinstance(dt.today(), dt) and dt.now().year >= 2024
assert dt.now(timezone.utc).tzinfo is timezone.utc and dt.utcnow().tzinfo is None

# timestamps (aware conversions are exact; naive ones depend on the local zone, so only round-trip)
epoch = dt(1970, 1, 1, tzinfo=timezone.utc)
assert epoch.timestamp() == 0.0 and dt(2024, 1, 1, tzinfo=timezone.utc).timestamp() == 1704067200.0
assert dt.fromtimestamp(0, timezone.utc) == epoch and dt.fromtimestamp(1704067200, tz=timezone.utc) == dt(2024, 1, 1, tzinfo=timezone.utc)
assert dt.utcfromtimestamp(0) == dt(1970, 1, 1) and dt.utcfromtimestamp(1704067200.5) == dt(2024, 1, 1, 0, 0, 0, 500000)
assert dt.fromtimestamp(86400.25, timezone.utc).microsecond == 250000
local = dt(2024, 3, 15, 12, 30)
assert dt.fromtimestamp(local.timestamp()) == local
assert dt(2024, 1, 1, 1, tzinfo=timezone(timedelta(hours=1))).timestamp() == 1704067200.0

# arithmetic and comparison
assert n + timedelta(days=1, hours=1) == dt(2024, 5, 7, 8, 8, 9, 10) and n - timedelta(microseconds=10) == dt(2024, 5, 6, 7, 8, 9)
assert dt(2024, 5, 7) - dt(2024, 5, 6, 12) == timedelta(hours=12) and dt(2024, 1, 1) - dt(2023, 1, 1) == timedelta(days=365)
assert n < dt(2024, 5, 6, 7, 8, 10) and n == dt(2024, 5, 6, 7, 8, 9, 10) and hash(n) == hash(dt(2024, 5, 6, 7, 8, 9, 10))
assert sorted([dt(2024, 1, 2), dt(2024, 1, 1)])[0] == dt(2024, 1, 1) and n != dt(2024, 5, 6)
assert dt(2024, 5, 6) != date(2024, 5, 6)                              # a datetime never equals a date
try:
    n - date(2024, 1, 1)
    assert False
except TypeError:
    pass
try:
    n < dt(2024, 5, 6, tzinfo=timezone.utc)                            # naive vs aware
    assert False
except TypeError:
    pass
try:
    n - dt(2024, 5, 6, tzinfo=timezone.utc)
    assert False
except TypeError:
    pass
try:
    dt(2024, 2, 30)
    assert False
except ValueError:
    pass
try:
    dt(2024, 1, 1, 24)
    assert False
except ValueError:
    pass
try:
    dt.max + timedelta(days=1)
    assert False
except OverflowError:
    pass

# aware datetimes
utc = timezone.utc
plus2 = timezone(timedelta(hours=2))
aw = dt(2024, 5, 6, 12, 0, tzinfo=plus2)
assert aw.utcoffset() == timedelta(hours=2) and aw.tzname() == 'UTC+02:00' and aw.dst() is None
assert aw.isoformat() == '2024-05-06T12:00:00+02:00' and str(aw) == '2024-05-06 12:00:00+02:00'
assert repr(aw).endswith('tzinfo=datetime.timezone(datetime.timedelta(seconds=7200)))')
assert aw == dt(2024, 5, 6, 10, 0, tzinfo=utc) and aw - dt(2024, 5, 6, 10, 0, tzinfo=utc) == timedelta(0)
assert aw > dt(2024, 5, 6, 9, 59, tzinfo=utc) and hash(aw) == hash(dt(2024, 5, 6, 10, 0, tzinfo=utc))
assert aw.strftime('%z') == '+0200' and aw.strftime('%Z') == 'UTC+02:00' and dt(2024, 1, 1, tzinfo=utc).strftime('%z %Z') == '+0000 UTC'
# dialect: glibc's flags and extra codes, as CPython on Linux/macOS (Windows CPython refuses %-d/%s/%P)
assert dt(2024, 1, 2, 3, 4, 5).strftime('%-d.%-m.%Y %-H:%M') == '2.1.2024 3:04'
assert dt(2005, 1, 2).strftime('%-y|%_d|%_j|%0e|%^b|%#B') == '5| 2|  2|02|JAN|JANUARY'
assert dt(2024, 1, 2, 13, 4, 5).strftime('%e|%k|%l|%P|%C|%h') == ' 2|13| 1|pm|20|Jan'
assert dt(2024, 1, 2, 13, 4, 5).strftime('%D %F %T %R %r') == '01/02/24 2024-01-02 13:04:05 13:04 01:04:05 PM'
assert dt(2021, 1, 3).strftime('%G-W%V-%u') == '2020-W53-7' and dt(2024, 12, 30).strftime('%G-W%V-%u') == '2025-W01-1'
assert dt(2024, 1, 2).strftime('%-Q') == '%-Q'                          # an unknown code stays literal, flag and all
# astimezone: the same instant under another zone; a naive datetime is read as local time
assert dt(2024, 1, 1, 12, tzinfo=utc).astimezone(timezone(timedelta(hours=3))).isoformat() == '2024-01-01T15:00:00+03:00'
assert dt(2024, 1, 1, 12, tzinfo=timezone(timedelta(hours=-5))).astimezone(utc).isoformat() == '2024-01-01T17:00:00+00:00'
assert dt(2024, 3, 31, 1, tzinfo=utc).astimezone(timezone(timedelta(hours=-2))).day == 30
local = dt(2024, 1, 1, 12).astimezone()
assert local.tzinfo is not None and local.astimezone(utc) == dt(2024, 1, 1, 12).astimezone(utc)
assert dt(2024, 1, 1, tzinfo=utc).astimezone(utc) == dt(2024, 1, 1, tzinfo=utc)
try:
    dt(2024, 1, 1).astimezone(1)
    assert False
except TypeError as e:
    assert str(e) == 'tz argument must be an instance of tzinfo'
assert aw.utctimetuple()[:5] == (2024, 5, 6, 10, 0) and aw.timetz() == time(12, 0, tzinfo=plus2)
assert (aw + timedelta(hours=1)).isoformat() == '2024-05-06T13:00:00+02:00'
named = timezone(timedelta(hours=-5, minutes=-30), 'X')
assert named.tzname(None) == 'X' and named.utcoffset(None) == timedelta(hours=-5, minutes=-30) and named.dst(None) is None
assert dt(2024, 1, 1, tzinfo=named).isoformat() == '2024-01-01T00:00:00-05:30' and dt(2024, 1, 1, tzinfo=named).strftime('%z') == '-0530'
assert timezone(timedelta(0)) == utc and timezone(timedelta(hours=1)) == timezone(timedelta(hours=1)) and timezone(timedelta(hours=1)) != plus2
assert repr(utc) == 'datetime.timezone.utc' and str(utc) == 'UTC' and repr(plus2) == 'datetime.timezone(datetime.timedelta(seconds=7200))'
assert str(plus2) == 'UTC+02:00' and str(named) == 'X' and utc.tzname(None) == 'UTC' and utc.utcoffset(None) == timedelta(0)
assert utc.fromutc(dt(2024, 1, 1, tzinfo=utc)) == dt(2024, 1, 1, tzinfo=utc)
assert plus2.fromutc(dt(2024, 1, 1, tzinfo=plus2)) == dt(2024, 1, 1, 2, tzinfo=plus2)
assert isinstance(utc, datetime.tzinfo) and isinstance(plus2, timezone)
# 3.7+: offsets need not be whole minutes; isoformat/tzname/%z carry the seconds and microseconds
tz30 = timezone(timedelta(seconds=30))
assert tz30.utcoffset(None) == timedelta(seconds=30) and tz30.tzname(None) == 'UTC+00:00:30'
assert dt(2020, 1, 1, tzinfo=timezone(timedelta(hours=1, seconds=30))).isoformat() == '2020-01-01T00:00:00+01:00:30'
assert dt(2020, 1, 1, tzinfo=timezone(timedelta(microseconds=1))).isoformat() == '2020-01-01T00:00:00+00:00:00.000001'
assert dt(2020, 1, 1, tzinfo=timezone(timedelta(hours=5, seconds=7))).strftime('%z') == '+050007'
assert dt(2020, 1, 1, tzinfo=timezone(timedelta(hours=2))).isoformat() == '2020-01-01T00:00:00+02:00'
assert timezone(timedelta(seconds=30)) == timezone(timedelta(seconds=30), 'named')
try:
    timezone(timedelta(hours=24))
    assert False
except ValueError:
    pass
try:
    timezone(3600)
    assert False
except TypeError:
    pass
try:
    utc.fromutc(dt(2024, 1, 1))
    assert False
except ValueError:
    pass

# strptime
assert dt.strptime('2024-05-06 07:08:09', '%Y-%m-%d %H:%M:%S') == dt(2024, 5, 6, 7, 8, 9)
assert dt.strptime('06/05/24', '%d/%m/%y') == dt(2024, 5, 6) and dt.strptime('Mon May  6 2024', '%a %b %d %Y') == dt(2024, 5, 6)
assert dt.strptime('07:08 PM', '%I:%M %p') == dt(1900, 1, 1, 19, 8) and dt.strptime('2024-127', '%Y-%j') == dt(2024, 5, 6)
assert dt.strptime('2024-05-06T07:08:09.000010', '%Y-%m-%dT%H:%M:%S.%f') == n
assert dt.strptime('2024-05-06 +0200', '%Y-%m-%d %z').utcoffset() == timedelta(hours=2)
# 3.7+: %z takes a colon, seconds, a fraction, and a literal Z; a zero offset is the utc singleton
assert dt.strptime('2024-05-06T07:08:09+02:00', '%Y-%m-%dT%H:%M:%S%z').utcoffset() == timedelta(hours=2)
assert dt.strptime('+04:30:15', '%z').utcoffset() == timedelta(hours=4, minutes=30, seconds=15)
assert dt.strptime('-05:30:15.000250', '%z').utcoffset() == -timedelta(hours=5, minutes=30, seconds=15, microseconds=250)
assert dt.strptime('Z', '%z').tzinfo is timezone.utc and dt.strptime('+00:00', '%z').tzinfo is timezone.utc
assert timezone(timedelta(0)) is timezone.utc and timezone(timedelta(0), 'UTC') is not timezone.utc
try:
    dt.strptime('+04:0030', '%z')
    assert False
except ValueError as e:
    assert str(e) == 'Inconsistent use of : in +04:0030'
assert dt.strptime('12 AM', '%I %p').hour == 0 and dt.strptime('12 PM', '%I %p').hour == 12
assert dt.strptime('February 29 2024', '%B %d %Y') == dt(2024, 2, 29) and dt.strptime('x2024x', 'x%Yx').year == 2024
assert dt.strptime('99', '%y').year == 1999 and dt.strptime('68', '%y').year == 2068 and dt.strptime('69', '%y').year == 1969
for bad in (('2024-13-01', '%Y-%m-%d'), ('2024-05-06', '%Y/%m/%d'), ('2023-02-29', '%Y-%m-%d'), ('abc', '%Y'), ('2024-05-06 x', '%Y-%m-%d')):
    try:
        dt.strptime(*bad)
        assert False, bad
    except ValueError:
        pass

# ================= mixing and idioms =================
assert isinstance(n, date) and isinstance(n, dt) and not isinstance(d, dt)
start = dt(2024, 1, 31)
assert [(start + timedelta(days=i)).strftime('%d.%m') for i in range(3)] == ['31.01', '01.02', '02.02']
days = (date(2024, 12, 25) - date(2024, 1, 1)).days
assert days == 359
deadline = dt(2024, 5, 6, 9, 0)
assert deadline - n == timedelta(hours=1, minutes=51, seconds=50, microseconds=999990)
assert (deadline - n) > timedelta(hours=1) and (deadline - n).total_seconds() > 6000
week = [date(2024, 5, 6) + timedelta(days=i) for i in range(7)]
assert [w.strftime('%a') for w in week] == ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun']
assert max(week) == date(2024, 5, 12) and week[-1].isoweekday() == 7

# ================= fromisoformat (3.11+ grammar) =================
assert dt.fromisoformat('2011-11-04') == dt(2011, 11, 4) and dt.fromisoformat('20111104') == dt(2011, 11, 4)
assert dt.fromisoformat('2011-11-04T00:05:23') == dt(2011, 11, 4, 0, 5, 23)
assert dt.fromisoformat('2011-11-04 00:05:23.283') == dt(2011, 11, 4, 0, 5, 23, 283000)
assert dt.fromisoformat('2011-11-04T00:05:23,283') == dt(2011, 11, 4, 0, 5, 23, 283000)
assert dt.fromisoformat('2011-11-04T00:05:23Z') == dt(2011, 11, 4, 0, 5, 23, tzinfo=timezone.utc)
assert dt.fromisoformat('2011-11-04T00:05:23Z').tzinfo is timezone.utc
assert dt.fromisoformat('2011-11-04T00:05:23+00:00').tzinfo is timezone.utc
assert dt.fromisoformat('2011-11-04T00:05:23+04:00').utcoffset() == timedelta(hours=4)
assert dt.fromisoformat('2011-11-04T00:05:23-05:30').utcoffset() == timedelta(hours=-5, minutes=-30)
assert dt.fromisoformat('2011-11-04T00:05:23.283+04:00') == dt(2011, 11, 4, 0, 5, 23, 283000, tzinfo=timezone(timedelta(hours=4)))
assert dt.fromisoformat('20111104T000523') == dt(2011, 11, 4, 0, 5, 23)
assert dt.fromisoformat('2011-W01-2T00:05:23.283') == dt(2011, 1, 4, 0, 5, 23, 283000)
assert dt.fromisoformat('2011-W01') == dt(2011, 1, 3) and dt.fromisoformat('2011W012') == dt(2011, 1, 4)
assert dt.fromisoformat('2011-11-04T24:00:00') == dt(2011, 11, 5)
assert dt.fromisoformat('2011-11-04T00:05:23.1234567') == dt(2011, 11, 4, 0, 5, 23, 123456)
aware = dt(2024, 2, 29, 13, 45, 30, 123456, tzinfo=timezone(timedelta(hours=3)))
assert dt.fromisoformat(aware.isoformat()) == aware and dt.fromisoformat(str(aware)) == aware
assert date.fromisoformat('2019-12-04') == date(2019, 12, 4) and date.fromisoformat('20191204') == date(2019, 12, 4)
assert date.fromisoformat('2021-W01-1') == date(2021, 1, 4) and date.fromisoformat(date(2024, 2, 29).isoformat()) == date(2024, 2, 29)
assert time.fromisoformat('04:23:01') == time(4, 23, 1) and time.fromisoformat('T04:23:01') == time(4, 23, 1)
assert time.fromisoformat('04:23:01.000384') == time(4, 23, 1, 384) and time.fromisoformat('0423') == time(4, 23)
assert time.fromisoformat('04:23:01+04:00') == time(4, 23, 1, tzinfo=timezone(timedelta(hours=4)))
assert time.fromisoformat('04:23:01Z').tzinfo is timezone.utc and time.fromisoformat('24:00') == time(0, 0)
for bad in ['abc', '2011-11-04T00:05:23+4', '2011-11-04T00:0523', '2011-1104', '2011-W54-1', '2011-W01-8', '2011-11-04T00:05:23.']:
    try:
        dt.fromisoformat(bad)
        assert False, bad
    except ValueError as e:
        assert str(e) == 'Invalid isoformat string: ' + repr(bad)
try:
    dt.fromisoformat('2011-13-04')
    assert False
except ValueError as e:
    assert str(e) == 'month must be in 1..12'                       # range errors keep the constructor's text
try:
    dt.fromisoformat('2011-11-04T24:01')
    assert False
except ValueError as e:
    assert str(e) == 'minute, second, and microsecond must be 0 when hour is 24'
try:
    date.fromisoformat('2019-12-04T00:00')
    assert False
except ValueError:
    pass
try:
    dt.fromisoformat(1)
    assert False
except TypeError as e:
    assert str(e) == 'fromisoformat: argument must be str'
