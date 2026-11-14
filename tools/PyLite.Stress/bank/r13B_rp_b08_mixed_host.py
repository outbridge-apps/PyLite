# repeat: 2
# a typical host job: json in, csv out, Decimal money, dates, grouping, sorting, f-strings
import json, csv, io, time
from decimal import Decimal, ROUND_HALF_UP
from datetime import datetime, timedelta, date
from collections import defaultdict, Counter, OrderedDict, namedtuple
import itertools
rows = [{'id': i, 'cust': 'C%03d' % (i % 400), 'amount': '%d.%02d' % (i % 5000, i % 100), 'date': (date(2026, 1, 1) + timedelta(days=i % 300)).isoformat(),
         'tags': ['t%d' % (i % 7), 't%d' % (i % 3)]} for i in range(40000)]
src = json.dumps(rows)
t0 = time.perf_counter()
data = json.loads(src)
by = defaultdict(lambda: Decimal('0'))
months = Counter()
Rec = namedtuple('Rec', 'cust amount day')
recs = []
for r in data:
    amt = Decimal(r['amount']).quantize(Decimal('0.01'), rounding=ROUND_HALF_UP)
    d = datetime.strptime(r['date'], '%Y-%m-%d')
    by[r['cust']] += amt * Decimal('1.21')
    months[d.strftime('%Y-%m')] += 1
    recs.append(Rec(r['cust'], amt, d.date()))
t1 = time.perf_counter()
recs.sort(key=lambda x: (x.day, -x.amount, x.cust))
groups = {k: sum(1 for _ in g) for k, g in itertools.groupby(recs, key=lambda x: x.day.month)}
out = io.StringIO()
w = csv.writer(out)
w.writerow(['cust', 'total'])
for k in sorted(by):
    w.writerow([k, f'{by[k]:,.2f}'])
back = list(csv.reader(io.StringIO(out.getvalue())))
t2 = time.perf_counter()
s = []
for r in recs[:20000]:
    s.append(f'{r.cust:<6}|{r.amount:>10}|{r.day:%d.%m.%Y}')
txt = '\n'.join(s).upper().replace('|', ';')
t3 = time.perf_counter()
print(len(by), months.most_common(2), groups.get(1), len(back), len(txt), json.dumps(OrderedDict(sorted(months.items())[:2])),
      'parse', round(t1 - t0, 3), 'group', round(t2 - t1, 3), 'fmt', round(t3 - t2, 3))
