import datetime as dt

out = []
plus3 = dt.timezone(dt.timedelta(hours=3))
minus5 = dt.timezone(dt.timedelta(hours=-5), 'EST')
t = dt.datetime(2026, 10, 29, 12, 0, tzinfo=plus3)
out.append(str(t))
out.append(t.isoformat())
out.append(str(t.astimezone(dt.timezone.utc)))
out.append(str(t.astimezone(minus5)))
out.append(t.astimezone(minus5).tzname())
out.append(str(t.utcoffset()))
out.append(t == t.astimezone(minus5))
out.append(str(t - dt.datetime(2026, 10, 29, 12, 0, tzinfo=dt.timezone.utc)))
out.append(str(dt.timezone.utc))
out.append(plus3.tzname(None))
out.append(str(t.replace(tzinfo=None)))
out.append(t.strftime('%z %Z'))
out.append(dt.datetime(2026, 1, 1, tzinfo=dt.timezone.utc).timestamp())
out.append(str(dt.datetime.fromtimestamp(0, dt.timezone.utc)))
result = out
