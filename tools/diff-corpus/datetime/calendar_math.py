import datetime as dt

out = []
for y, m, d in [(2026, 1, 1), (2024, 2, 29), (2000, 12, 31), (1999, 1, 4)]:
    x = dt.date(y, m, d)
    out.append((x.weekday(), x.isoweekday(), tuple(x.isocalendar()), x.toordinal(), x.timetuple().tm_yday))
out.append(str(dt.date.fromordinal(739000)))
out.append(str(dt.datetime.strptime('2026-10-29 14:05', '%Y-%m-%d %H:%M')))
out.append(str(dt.datetime.strptime('29/10/26', '%d/%m/%y')))
out.append(str(dt.datetime.strptime('Oct 29 2026', '%b %d %Y')))
out.append(str(dt.datetime.strptime('2026-302', '%Y-%j')))
out.append(str(dt.datetime.combine(dt.date(2026, 1, 2), dt.time(3, 4))))
out.append(str(dt.datetime(2026, 1, 2, 3, 4).replace(year=2025, minute=0)))
out.append(str(dt.date(2026, 1, 31).replace(day=1)))
out.append(dt.datetime(2026, 1, 2, 3, 4).date() == dt.date(2026, 1, 2))
out.append(str(dt.datetime(2026, 1, 2, 3, 4).time()))
out.append(max(dt.date(2026, 1, 1), dt.date(2025, 12, 31)).year)
out.append(sorted([dt.time(12), dt.time(1), dt.time(23, 59)])[0].hour)
out.append((dt.date.min.year, dt.date.max.year, str(dt.timedelta.max)))
result = out
