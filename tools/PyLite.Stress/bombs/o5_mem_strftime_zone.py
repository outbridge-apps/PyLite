# deadline: 3000
# allocmb: 256
from datetime import datetime, timezone, timedelta
tz = timezone(timedelta(0), 'x' * 1000000)
x = datetime(2020, 1, 1, tzinfo=tz).strftime('%Z' * 300)
