# repeat: 2
import string, zoneinfo, datetime
t = 0
for k in range(200):
    t += len(string.Template("$a-$b").substitute(a=k, b=k))
    z = zoneinfo.ZoneInfo("America/New_York")
    t += datetime.datetime(2024, 3, 10, 3, tzinfo=z).utcoffset().seconds
    z2 = zoneinfo.ZoneInfo.no_cache("Europe/Paris")
print(t)
