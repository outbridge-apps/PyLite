import datetime as dt


def err(f):
    try:
        f()
        return 'ok'
    except Exception as e:
        return type(e).__name__


aware = dt.datetime(2026, 1, 1, tzinfo=dt.timezone.utc)
naive = dt.datetime(2026, 1, 1)
out = []
out.append(err(lambda: dt.date(2026, 2, 30)))
out.append(err(lambda: dt.date(2026, 13, 1)))
out.append(err(lambda: dt.time(24, 0)))
out.append(err(lambda: dt.timedelta(days=10 ** 9)))
out.append(err(lambda: dt.date.max + dt.timedelta(days=1)))
out.append(err(lambda: aware < naive))
out.append(err(lambda: aware - naive))
out.append(err(lambda: dt.datetime.strptime('2026-10', '%Y-%m-%d')))
out.append(err(lambda: dt.datetime.strptime('x', '%Y')))
out.append(err(lambda: dt.date(2026, 1, 1) + 1))
out.append(err(lambda: dt.timezone(dt.timedelta(hours=25))))
result = out
