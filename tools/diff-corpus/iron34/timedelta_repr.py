# CPython 3.7+ (and PyLite) name the fields: datetime.timedelta(days=-1, seconds=82800);
# IronPython 3.4.2 prints the 3.4 form datetime.timedelta(-1, 82800).
import datetime
result = repr(datetime.timedelta(hours=-1))
