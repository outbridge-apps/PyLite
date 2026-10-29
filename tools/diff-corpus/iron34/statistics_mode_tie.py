# CPython 3.8+ (and PyLite): mode() of data with several modes answers the first one met;
# Python 3.4, and IronPython 3.4.2, raise StatisticsError ("no unique mode").
import statistics
try:
    result = statistics.mode([1, 1, 2, 2, 3])
except statistics.StatisticsError:
    result = 'StatisticsError'
