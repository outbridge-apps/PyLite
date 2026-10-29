import datetime
from email.utils import formatdate, format_datetime, parsedate, parsedate_tz, parsedate_to_datetime, mktime_tz
tz = datetime.timezone(datetime.timedelta(hours=-7))
aware = datetime.datetime(2001, 9, 23, 20, 10, 55, tzinfo=tz)
naive = datetime.datetime(2001, 9, 23, 20, 10, 55)
result = [formatdate(0), formatdate(1000000000, usegmt=True), formatdate(1234567890.5),
          format_datetime(naive), format_datetime(aware),
          parsedate_tz('Wed, 3 Apr 2002 14:58:26 +0800'), parsedate_tz('5 Feb 2003 13:47:26 -0800'),
          parsedate_tz('Friday, 19-Nov-82 16:14:55 EST'), parsedate('Wed, 3 Apr 2002 14:58 +0800'),
          parsedate_tz('25 Feb 03 13:47:26 -0800'), parsedate('A Complete Waste of Time'),
          str(parsedate_to_datetime('Sun, 23 Sep 2001 20:10:55 -0700')),
          str(parsedate_to_datetime('Sun, 23 Sep 2001 20:10:55 -0000')),
          mktime_tz((1970, 1, 1, 0, 0, 0, -1, -1, -1, 1234)),
          mktime_tz(parsedate_tz('Mon, 20 Nov 1995 19:12:08 -0500'))]
