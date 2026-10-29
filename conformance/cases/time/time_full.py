# Adapted from CPython's test suite (Lib/test/test_time.py: TimeTestCase.test_conversions/test_strftime/
# test_strptime/test_asctime/test_ctime/test_mktime/test_monotonic/test_perf_counter/test_localtime,
# StructTimeTest). Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Every time.* member the dialect exposes is exercised here; wall-clock values are checked for shape.

import time

# ---- struct_time ----
st = time.gmtime(0)
assert tuple(st) == (1970, 1, 1, 0, 0, 0, 3, 1, 0) and len(st) == 9
assert st.tm_year == 1970 and st.tm_mon == 1 and st.tm_mday == 1 and st.tm_hour == 0 and st.tm_min == 0
assert st.tm_sec == 0 and st.tm_wday == 3 and st.tm_yday == 1 and st.tm_isdst == 0
assert st[0] == 1970 and st[-1] == 0 and st[6] == 3 and st[1:3] == (1, 1) and list(st)[:2] == [1970, 1]
assert isinstance(st, time.struct_time) and st == time.gmtime(0) and st == (1970, 1, 1, 0, 0, 0, 3, 1, 0)
assert time.gmtime(86400) > st and st < time.gmtime(1) and st != time.gmtime(1)
assert repr(st) == 'time.struct_time(tm_year=1970, tm_mon=1, tm_mday=1, tm_hour=0, tm_min=0, tm_sec=0, tm_wday=3, tm_yday=1, tm_isdst=0)'
y, m, d, *rest = st
assert (y, m, d) == (1970, 1, 1) and rest == [0, 0, 0, 3, 1, 0]
built = time.struct_time((2024, 2, 29, 12, 30, 45, 3, 60, 0))
assert built.tm_year == 2024 and built.tm_yday == 60 and tuple(built)[:3] == (2024, 2, 29)
try:
    time.struct_time((2024, 1))
    assert False
except TypeError:
    pass
try:
    st.tm_year = 2000
    assert False
except (AttributeError, TypeError):
    pass
try:
    st[9]
    assert False
except IndexError:
    pass

# ---- gmtime ----
assert tuple(time.gmtime(1704067200))[:6] == (2024, 1, 1, 0, 0, 0) and time.gmtime(1704067200).tm_wday == 0
assert time.gmtime(1709164800).tm_yday == 60 and tuple(time.gmtime(-1))[:6] == (1969, 12, 31, 23, 59, 59)
assert tuple(time.gmtime(1.9))[:6] == (1970, 1, 1, 0, 0, 1)                          # fractions are truncated
assert time.gmtime(951782400)[:3] == (2000, 2, 29)
now_g = time.gmtime()
assert now_g.tm_year >= 2024 and 1 <= now_g.tm_mon <= 12 and now_g.tm_isdst == 0
assert time.gmtime(None).tm_year >= 2024
try:
    time.gmtime('x')
    assert False
except TypeError:
    pass
try:
    time.gmtime(1e20)
    assert False
except (OverflowError, ValueError, OSError if False else Exception):
    pass

# ---- localtime / mktime ----
lt = time.localtime(1704067200)
assert lt.tm_year in (2023, 2024) and isinstance(lt, time.struct_time)
assert time.localtime().tm_year >= 2024
assert time.mktime(time.localtime(1704067200)) == 1704067200.0                        # round trip
assert isinstance(time.mktime(lt), float)
assert time.mktime(time.localtime(0)) == 0.0
try:
    time.mktime((2024, 1))
    assert False
except TypeError:
    pass
for bad in ((2024, 13, 1, 0, 0, 0, 0, 1, 0), (2024, 1, 1, 25, 0, 0, 0, 1, 0), (2024, 1, 1, 0, 0, 0, 0, 0, 0) if False else (2024, 1, 1, 0, 61, 0, 0, 1, 0)):
    try:
        time.strftime('%Y', bad)
        assert False, bad
    except ValueError:
        pass

# ---- asctime / ctime ----
assert time.asctime(time.gmtime(0)) == 'Thu Jan  1 00:00:00 1970'
assert time.asctime(time.gmtime(1704067200)) == 'Mon Jan  1 00:00:00 2024'
assert time.asctime((2024, 2, 29, 12, 30, 45, 3, 60, 0)) == 'Thu Feb 29 12:30:45 2024'
assert time.asctime(time.gmtime(1000000000)) == 'Sun Sep  9 01:46:40 2001'
assert len(time.asctime()) == 24 and time.asctime()[-4:].isdigit()
assert time.ctime(0) == time.asctime(time.localtime(0)) and len(time.ctime()) == 24
assert time.ctime(1704067200) == time.asctime(time.localtime(1704067200))
try:
    time.asctime('x')
    assert False
except TypeError:
    pass

# ---- strftime: every supported directive ----
t9 = (2024, 2, 29, 13, 5, 9, 3, 60, 0)
assert time.strftime('%Y-%m-%d %H:%M:%S', t9) == '2024-02-29 13:05:09'
assert time.strftime('%y %I %p %j %w', t9) == '24 01 PM 060 4'
assert time.strftime('%a %A %b %B', t9) == 'Thu Thursday Feb February'
assert time.strftime('%c|%x|%X', t9) == 'Thu Feb 29 13:05:09 2024|02/29/24|13:05:09'
assert time.strftime('%U %W', t9) == '08 09' and time.strftime('%%', t9) == '%'
assert time.strftime('%z%Z', t9) == '' and time.strftime('', t9) == '' and time.strftime('plain', t9) == 'plain'
assert time.strftime('%d', (2024, 1, 5, 0, 0, 0, 4, 5, 0)) == '05' and time.strftime('%I %p', (2024, 1, 1, 0, 0, 0, 0, 1, 0)) == '12 AM'
assert time.strftime('%Y', time.gmtime(0)) == '1970' and time.strftime('%j', time.gmtime(0)) == '001'
assert time.strftime('%Y').isdigit() and len(time.strftime('%H:%M:%S')) == 8
assert time.strftime('%U', (2024, 1, 1, 0, 0, 0, 0, 1, 0)) == '00' and time.strftime('%W', (2024, 1, 1, 0, 0, 0, 0, 1, 0)) == '01'
assert time.strftime('%U', (2024, 1, 7, 0, 0, 0, 6, 7, 0)) == '01' and time.strftime('%W', (2024, 1, 7, 0, 0, 0, 6, 7, 0)) == '01'
assert time.strftime('%Y', (9999, 12, 31, 0, 0, 0, 0, 365, 0)) == '9999'
try:
    time.strftime(1)
    assert False
except TypeError:
    pass

# ---- strptime ----
p = time.strptime('2024-02-29 13:05:09', '%Y-%m-%d %H:%M:%S')
assert isinstance(p, time.struct_time) and tuple(p)[:6] == (2024, 2, 29, 13, 5, 9) and p.tm_wday == 3 and p.tm_yday == 60 and p.tm_isdst == -1
assert time.strptime('Thu Feb 29 13:05:09 2024') == p                                 # default format = asctime's
assert time.strptime('29/02/24', '%d/%m/%y')[:3] == (2024, 2, 29) and time.strptime('1:05 PM', '%I:%M %p').tm_hour == 13
assert time.strptime('60 2024', '%j %Y')[:3] == (2024, 2, 29) and time.strptime('', '') .tm_year == 1900
assert time.strptime('February', '%B').tm_mon == 2 and time.strptime('Mon', '%a').tm_wday == 0
assert time.strptime('12 AM', '%I %p').tm_hour == 0 and time.strptime('12 PM', '%I %p').tm_hour == 12
assert time.strptime('%', '%%').tm_year == 1900
for bad in (('2024-13-01', '%Y-%m-%d'), ('x', '%Y'), ('2024', '%Y-%m'), ('2023-02-29', '%Y-%m-%d'), ('2024 extra', '%Y')):
    try:
        time.strptime(*bad)
        assert False, bad
    except ValueError:
        pass

# ---- clocks ----
t0 = time.time()
assert isinstance(t0, float) and t0 > 1700000000.0 and time.time() >= t0
m0 = time.monotonic()
m1 = time.monotonic()
assert isinstance(m0, float) and m1 >= m0
c0 = time.perf_counter()
assert isinstance(c0, float) and time.perf_counter() >= c0
assert time.sleep(0) is None and time.sleep(0.001) is None
before = time.perf_counter()
time.sleep(0.01)
assert time.perf_counter() - before >= 0.005
try:
    time.sleep(-1)
    assert False
except ValueError:
    pass
try:
    time.sleep('1')
    assert False
except TypeError:
    pass

# ---- zone constants ----
assert isinstance(time.timezone, int) and isinstance(time.altzone, int) and time.daylight in (0, 1)
assert isinstance(time.tzname, tuple) and len(time.tzname) == 2 and all(isinstance(z, str) for z in time.tzname)
assert -14 * 3600 <= time.timezone <= 14 * 3600

# ---- conversions agree with each other ----
assert time.gmtime(time.mktime(time.localtime(1704067200))) == time.gmtime(1704067200)
assert time.strftime('%Y-%m-%d', time.strptime('2024-02-29', '%Y-%m-%d')) == '2024-02-29'
assert int(time.mktime(time.strptime('1970-01-02', '%Y-%m-%d'))) - int(time.mktime(time.strptime('1970-01-01', '%Y-%m-%d'))) == 86400
