# Adapted from CPython's test suite (Lib/test/test_zoneinfo/test_zoneinfo.py: ZoneInfoTest.test_str /
# test_utc / test_unambiguous / test_folds_and_gaps, ZoneInfoCacheTest, ZoneInfoDatetimeSubclassTest).
# Python Software Foundation, PSF License Version 2 -- see README.
# dialect: the zone rules come from the Windows time zone database through CLDR's IANA map, so the
# offsets are tzdata's for current rules; tzname() is tzdata's abbreviation from an engine table.
from datetime import datetime, time, timedelta, timezone, tzinfo
from zoneinfo import ZoneInfo, available_timezones, ZoneInfoNotFoundError

ams = ZoneInfo('Europe/Amsterdam')
utc = timezone.utc
assert str(ams) == 'Europe/Amsterdam' and ams.key == 'Europe/Amsterdam' and repr(ams) == "zoneinfo.ZoneInfo(key='Europe/Amsterdam')"
assert isinstance(ams, tzinfo)
assert ZoneInfo('Europe/Amsterdam') is ams and ZoneInfo.no_cache('Europe/Amsterdam') is not ams
assert ZoneInfo.no_cache('Europe/Amsterdam') == ams and len({ams, ZoneInfo('Europe/Amsterdam')}) == 1

# UTC to a zone and back, either side of DST
assert datetime(2024, 1, 1, 12, tzinfo=utc).astimezone(ams).isoformat() == '2024-01-01T13:00:00+01:00'
assert datetime(2024, 7, 1, 12, tzinfo=utc).astimezone(ams).isoformat() == '2024-07-01T14:00:00+02:00'
assert datetime(2024, 1, 1, 12, tzinfo=ams).astimezone(utc).isoformat() == '2024-01-01T11:00:00+00:00'
assert datetime(2024, 7, 1, 12, tzinfo=ams).astimezone(utc).isoformat() == '2024-07-01T10:00:00+00:00'
summer = datetime(2024, 7, 1, tzinfo=ams)
assert summer.utcoffset() == timedelta(hours=2) and summer.dst() == timedelta(hours=1)
assert datetime(2024, 1, 1, tzinfo=ams).dst() == timedelta(0)
assert summer == datetime(2024, 6, 30, 22, tzinfo=utc) and summer.timestamp() == 1719784800.0

# the spring-forward instant, the autumn ambiguous hour (fold=0 is the first occurrence) and the gap
assert datetime(2024, 3, 31, 0, 59, 59, tzinfo=utc).astimezone(ams).isoformat() == '2024-03-31T01:59:59+01:00'
assert datetime(2024, 3, 31, 1, 0, tzinfo=utc).astimezone(ams).isoformat() == '2024-03-31T03:00:00+02:00'
assert datetime(2024, 10, 27, 2, 30, tzinfo=ams).utcoffset() == timedelta(hours=2)
assert datetime(2024, 3, 31, 2, 30, tzinfo=ams).utcoffset() == timedelta(hours=1)

# other zones, aliases and the UTC keys
noon = datetime(2024, 7, 1, 12, tzinfo=utc)
assert noon.astimezone(ZoneInfo('America/New_York')).isoformat() == '2024-07-01T08:00:00-04:00'
assert noon.astimezone(ZoneInfo('Asia/Kolkata')).isoformat() == '2024-07-01T17:30:00+05:30'
assert noon.astimezone(ZoneInfo('Australia/Sydney')).isoformat() == '2024-07-01T22:00:00+10:00'
assert ZoneInfo('Europe/Kyiv').key == 'Europe/Kyiv' and ZoneInfo('Asia/Calcutta') != ZoneInfo('Asia/Kolkata')
assert datetime(2024, 1, 1, tzinfo=ZoneInfo('UTC')).utcoffset() == timedelta(0) and ZoneInfo('Etc/UTC').key == 'Etc/UTC'

# arithmetic keeps the zone and re-reads the offset; a time object gets utcoffset None from a zone
assert (datetime(2024, 3, 30, 12).replace(tzinfo=ams) + timedelta(days=1)).isoformat() == '2024-03-31T12:00:00+02:00'
assert time(12, 0, tzinfo=ams).utcoffset() is None and time(12, 0, tzinfo=ams).isoformat() == '12:00:00'
assert datetime.now(ams).tzinfo is ams and datetime.fromtimestamp(1719835200, ams).isoformat() == '2024-07-01T14:00:00+02:00'

# tzname: tzdata's abbreviations where it has them, its numeric form elsewhere
assert datetime(2024, 1, 1, tzinfo=ams).tzname() == 'CET' and summer.tzname() == 'CEST'
assert [datetime(2024, 7, 1, tzinfo=ZoneInfo(k)).tzname() for k in ['America/New_York', 'Europe/London', 'Asia/Kolkata', 'Asia/Dubai', 'UTC']] == ['EDT', 'BST', 'IST', '+04', 'UTC']

# fold (PEP 495): the second occurrence of the ambiguous hour
first = datetime(2024, 10, 27, 2, 30, tzinfo=ams)
second = datetime(2024, 10, 27, 2, 30, tzinfo=ams, fold=1)
assert first.fold == 0 and second.fold == 1 and repr(second).endswith("fold=1)") and first.replace(fold=1).fold == 1
assert first.utcoffset() == timedelta(hours=2) and second.utcoffset() == timedelta(hours=1)
assert (first.tzname(), second.tzname()) == ('CEST', 'CET')
assert second.timestamp() - first.timestamp() == 3600 and second.astimezone(utc).isoformat() == '2024-10-27T01:30:00+00:00'
assert first == second and len({first, second}) == 1                        # same tzinfo: wall clock compares, fold ignored
assert datetime(2024, 10, 27, 0, 30, tzinfo=utc).astimezone(ams).fold == 0
back = datetime(2024, 10, 27, 1, 30, tzinfo=utc).astimezone(ams)
assert back.fold == 1 and back.isoformat() == '2024-10-27T02:30:00+01:00' and back == second
assert datetime(2024, 3, 31, 2, 30, tzinfo=ams, fold=1).utcoffset() == timedelta(hours=2)    # a gap, fold=1: post-transition
try:
    datetime(2024, 1, 1, fold=2)
    assert False
except ValueError as e:
    assert str(e) == 'fold must be either 0 or 1'

assert 'Europe/Amsterdam' in available_timezones() and len(available_timezones()) > 300
assert issubclass(ZoneInfoNotFoundError, KeyError)
try:
    ZoneInfo('Mars/Olympus')
    assert False
except ZoneInfoNotFoundError as e:
    assert 'Mars/Olympus' in str(e)
try:
    ZoneInfo('Mars/Olympus')
    assert False
except KeyError:
    pass
