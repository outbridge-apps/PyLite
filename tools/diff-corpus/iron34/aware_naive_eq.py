# CPython 3.3+ (and PyLite): == between an aware and a naive datetime is False, only ordering raises;
# IronPython 3.4.2 raises TypeError for == as well.
import datetime
aware = datetime.datetime(2026, 1, 1, tzinfo=datetime.timezone.utc)
naive = datetime.datetime(2026, 1, 1)
result = (aware == naive, aware != naive)
