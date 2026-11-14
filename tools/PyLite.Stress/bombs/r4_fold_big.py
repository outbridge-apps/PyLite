# deadline: 2000
from datetime import datetime
x = 3 ** 600000
while True:
    try:
        datetime(2024, 1, 1, fold=x)
    except (ValueError, OverflowError):
        pass
