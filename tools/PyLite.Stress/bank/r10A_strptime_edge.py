from datetime import datetime, date
import time
cases = [("9999 52 7", "%G %V %u"), ("9999 52 5", "%G %V %u"), ("9999 366", "%Y %j"), ("9999 365", "%Y %j"),
         ("9999 53 0", "%Y %U %w"), ("9999 52 6", "%Y %W %w"), ("9999 53 1", "%Y %W %w"), ("1 1 1", "%G %V %u"),
         ("0001 001", "%Y %j"), ("2024 366", "%Y %j"), ("2023 366", "%Y %j"), ("9998 53 7", "%G %V %u"),
         ("9999 1 1", "%G %V %u"), ("2020 53 7", "%G %V %u"), ("2021 53 1", "%G %V %u")]
for s, f in cases:
    try:
        print(s, f, "->", datetime.strptime(s, f))
    except Exception as e:
        print(s, f, "->", type(e).__name__, e)
    try:
        print(s, f, "t->", tuple(time.strptime(s, f))[:7])
    except Exception as e:
        print(s, f, "t->", type(e).__name__, e)
try:
    date(10000, 1, 1)
except Exception as e:
    print("date", type(e).__name__, e)
try:
    print(date.fromisocalendar(9999, 52, 7))
except Exception as e:
    print("iso", type(e).__name__, e)
