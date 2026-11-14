import time
from decimal import Decimal, ROUND_HALF_UP, getcontext
from datetime import datetime, date, timedelta
t = time.perf_counter()
cent = Decimal("0.01")
tot = Decimal(0)
vat = Decimal("0.21")
for i in range(30000):
    net = Decimal(i % 1000) / Decimal(7) + Decimal("0.005")
    gross = (net * (1 + vat)).quantize(cent, rounding=ROUND_HALF_UP)
    tot += gross
s_tot = "{:,.2f}".format(tot)
t1 = time.perf_counter()
d0 = datetime(2026, 1, 1, 8, 30)
keys = {}
for i in range(20000):
    d = d0 + timedelta(hours=i * 7, minutes=i % 60)
    k = d.strftime("%Y-%m")
    keys[k] = keys.get(k, 0) + 1
    p = datetime.strptime(d.isoformat(), "%Y-%m-%dT%H:%M:%S")
    if p != d:
        raise ValueError(i)
t2 = time.perf_counter()
dd = [date(2026, 1, 1) + timedelta(days=i) for i in range(5000)]
wk = sum(1 for x in dd if x.weekday() < 5)
iso = [x.isocalendar()[1] for x in dd[:1000]]
fm = [x.strftime("%d.%m.%Y %A") for x in dd[:3000]]
t3 = time.perf_counter()
print(s_tot, len(keys), wk, iso[:3], fm[0], [round((b - a) * 1000) for a, b in ((t, t1), (t1, t2), (t2, t3))])
