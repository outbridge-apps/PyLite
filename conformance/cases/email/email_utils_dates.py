# Adapted from CPython's test suite (Lib/test/test_email/test_email.py: TestMiscellaneous date tests;
# Lib/test/test_email/test_utils.py: DateTimeTests). Python Software Foundation, PSF License
# Version 2 -- see conformance/README.md.
# dialect: email.utils has the date helpers only.
import time
import datetime
from email import utils
import email.utils

def raises(exc, f, *args):
    try:
        f(*args)
    except exc:
        return True
    return False

# ---- formatdate ----
now = time.time()
assert utils.parsedate(utils.formatdate(now))[:6] == time.gmtime(now)[:6]
assert utils.parsedate(utils.formatdate(now, localtime=True))[:6] == time.localtime(now)[:6]
assert utils.formatdate(now, localtime=False) == time.strftime('%a, %d %b %Y %H:%M:%S -0000', time.gmtime(now))
assert utils.formatdate(now, localtime=False, usegmt=True) == time.strftime('%a, %d %b %Y %H:%M:%S GMT', time.gmtime(now))
assert utils.formatdate(0) == 'Thu, 01 Jan 1970 00:00:00 -0000'
assert utils.formatdate(1000000000, usegmt=True) == 'Sun, 09 Sep 2001 01:46:40 GMT'
assert email.utils.formatdate is utils.formatdate

# ---- parsedate / parsedate_tz ----
invalid_dates = [
    '',
    ' ',
    '0',
    'A Complete Waste of Time',
    'Wed, 3 Apr 2002 12.34.56.78+0800',
    '17 June , 2022',
    'Friday, -Nov-82 16:14:55 EST',
    'Friday, Nov--82 16:14:55 EST',
    'Friday, 19-Nov- 16:14:55 EST',
]
for dtstr in invalid_dates:
    assert utils.parsedate(dtstr) is None, dtstr
    assert utils.parsedate_tz(dtstr) is None, dtstr
assert utils.parsedate(None) is None
assert utils.parsedate_tz(None) is None

assert utils.parsedate_tz('Wed, 3 Apr 2002 14:58:26 +0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, 28800)
assert utils.parsedate_tz('Wed,3 Apr 2002 14:58:26 +0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, 28800)
assert utils.parsedate_tz('Wed 3 Apr 2002 14:58:26 +0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, 28800)
assert utils.parsedate_tz('5 Feb 2003 13:47:26 -0800') == (2003, 2, 5, 13, 47, 26, 0, 1, -1, -28800)
assert utils.parsedate_tz('February 5, 2003 13:47:26 -0800') == (2003, 2, 5, 13, 47, 26, 0, 1, -1, -28800)
assert utils.parsedate_tz('Wed, 3 Apr 2002 14:58:26+0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, 28800)
assert utils.parsedate_tz('Wed, 3 Apr 2002 14:58:26-0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, -28800)
assert utils.parsedate_tz('5 Feb 2003 13.47.26 -0800') == (2003, 2, 5, 13, 47, 26, 0, 1, -1, -28800)
assert utils.parsedate_tz('5 Feb 2003 13.47 -0800') == (2003, 2, 5, 13, 47, 0, 0, 1, -1, -28800)
assert utils.parsedate_tz('Friday, 19-Nov-82 16:14:55 EST') == (1982, 11, 19, 16, 14, 55, 0, 1, -1, -18000)
assert utils.parsedate_tz('Wed, 3 Apr 2002 14:58 +0800') == (2002, 4, 3, 14, 58, 0, 0, 1, -1, 28800)
assert utils.parsedate_tz('Wed, 3 Apr 2002 14.58.26 +0800') == (2002, 4, 3, 14, 58, 26, 0, 1, -1, 28800)
assert utils.parsedate_tz('Wed, 3 Apr 2002 14.58 +0800') == (2002, 4, 3, 14, 58, 0, 0, 1, -1, 28800)
assert utils.parsedate_tz('25 Feb 03 13:47:26 -0800') == utils.parsedate_tz('25 Feb 2003 13:47:26 -0800')
assert utils.parsedate_tz('25 Feb 71 13:47:26 -0800') == utils.parsedate_tz('25 Feb 1971 13:47:26 -0800')
assert utils.parsedate_tz('Mon, 20 Nov 1995 19:12:08 GMT') == (1995, 11, 20, 19, 12, 8, 0, 1, -1, 0)
assert utils.parsedate_tz('Mon, 20 Nov 1995 19:12:08 -0000') == (1995, 11, 20, 19, 12, 8, 0, 1, -1, 0)
assert utils.parsedate('Mon, 20 Nov 1995 19:12:08 PST') == (1995, 11, 20, 19, 12, 8, 0, 1, -1)

# ---- the time functions accept the result ----
timetup = utils.parsedate('5 Feb 2003 13:47:26 -0800')
t = int(time.mktime(timetup))
assert time.localtime(t)[:6] == timetup[:6]
assert int(time.strftime('%Y', timetup)) == 2003

# ---- mktime_tz ----
assert utils.mktime_tz((1970, 1, 1, 0, 0, 0, -1, -1, -1, 0)) == 0
assert utils.mktime_tz((1970, 1, 1, 0, 0, 0, -1, -1, -1, 1234)) == -1234
assert utils.mktime_tz(utils.parsedate_tz('Mon, 20 Nov 1995 19:12:08 -0500')) == 816912728

# ---- format_datetime / parsedate_to_datetime (test_utils.DateTimeTests) ----
datestring = 'Sun, 23 Sep 2001 20:10:55'
dateargs = (2001, 9, 23, 20, 10, 55)
offsetstring = ' -0700'
tz = datetime.timezone(datetime.timedelta(hours=-7))
naive_dt = datetime.datetime(*dateargs)
aware_dt = datetime.datetime(*dateargs, tzinfo=tz)
assert utils.format_datetime(naive_dt) == datestring + ' -0000'
assert utils.format_datetime(aware_dt) == datestring + offsetstring
utc_dt = datetime.datetime(*dateargs, tzinfo=datetime.timezone.utc)
assert utils.format_datetime(utc_dt, usegmt=True) == datestring + ' GMT'
assert raises(ValueError, utils.format_datetime, naive_dt, True)
assert raises(ValueError, utils.format_datetime, aware_dt, True)
assert utils.parsedate_to_datetime(datestring + offsetstring) == aware_dt
assert utils.parsedate_to_datetime(datestring + ' -0000') == naive_dt
assert utils.parsedate_to_datetime(datestring + ' -0000').tzinfo is None
assert utils.parsedate_to_datetime('Mon, 20 Nov 1995 19:12:08 GMT').tzinfo == datetime.timezone.utc

invalid_to_datetime = [
    '',
    ' ',
    '0',
    'A Complete Waste of Time',
    'Wed, 3 Apr 2002 12.34.56.78+0800'
    'Tue, 06 Jun 2017 27:39:33 +0600',
    'Tue, 06 Jun 2017 07:39:33 +2600',
    'Tue, 06 Jun 2017 27:39:33',
    '17 June , 2022',
    'Friday, -Nov-82 16:14:55 EST',
    'Friday, Nov--82 16:14:55 EST',
    'Friday, 19-Nov- 16:14:55 EST',
]
for dtstr in invalid_to_datetime:
    assert raises(ValueError, utils.parsedate_to_datetime, dtstr), dtstr
