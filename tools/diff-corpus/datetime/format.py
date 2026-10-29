import datetime as dt

out = []
t = dt.datetime(2026, 1, 5, 7, 8, 9, 123456)
for f in ['%Y-%m-%d %H:%M:%S', '%y %j %U %W', '%a %A %b %B', '%I:%M %p', '%d/%m/%Y', '%%', '%f', '%w']:
    out.append(t.strftime(f))
out.append(t.isoformat())
out.append(t.isoformat(' '))
out.append(str(t))
out.append(repr(t))
out.append(dt.date(2026, 1, 5).isoformat())
out.append(dt.time(7, 8).isoformat())
out.append(str(dt.time(7, 8, 9, 10)))
out.append(dt.datetime(2026, 1, 5).ctime())
out.append(format(dt.date(2026, 12, 25), '%d %b'))
out.append('{:%Y/%m}'.format(dt.date(2026, 12, 25)))
result = out
