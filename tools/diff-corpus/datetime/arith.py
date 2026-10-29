import datetime as dt

out = []
d = dt.date(2024, 2, 28)
out.append(str(d + dt.timedelta(days=1)))
out.append(str(d + dt.timedelta(days=2)))
out.append((dt.date(2026, 1, 1) - dt.date(2024, 1, 1)).days)
td = dt.timedelta(days=1, hours=25, minutes=61, seconds=3661, microseconds=1500000)
out.append((td.days, td.seconds, td.microseconds, td.total_seconds()))
out.append(str(dt.timedelta(hours=-1)))
out.append(str(dt.timedelta(days=-1, seconds=5)))
out.append(str(dt.timedelta(weeks=3, milliseconds=7)))
out.append(dt.timedelta(hours=1) * 3 == dt.timedelta(hours=3))
out.append(dt.timedelta(hours=3) // dt.timedelta(minutes=50))
out.append(str(dt.timedelta(hours=3) % dt.timedelta(minutes=50)))
out.append(str(dt.timedelta(minutes=10) / 4))
out.append(str(abs(dt.timedelta(days=-2))))
t = dt.datetime(2026, 10, 29, 23, 30)
out.append(str(t + dt.timedelta(hours=1)))
out.append(str(t - dt.datetime(2026, 1, 1)))
out.append(str(dt.datetime(2026, 3, 31) - dt.timedelta(days=31)))
out.append(dt.timedelta(seconds=1) < dt.timedelta(minutes=1))
result = out
