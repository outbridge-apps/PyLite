# Adapted from CPython's test suite (Lib/test/test_collections.py subclass checks,
# Lib/test/datetimetester.py TestDateTime.test_subclass).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

from collections import Counter, defaultdict, OrderedDict, namedtuple
import datetime

# dict-family instances ARE dicts
assert isinstance(Counter(), dict)
assert isinstance(defaultdict(int), dict)
assert isinstance(OrderedDict(), dict)
assert isinstance(Counter(), Counter)
assert not isinstance({}, Counter)
assert not isinstance(Counter(), defaultdict)

# the type objects agree with the instances
assert issubclass(Counter, dict)
assert issubclass(defaultdict, dict)
assert issubclass(OrderedDict, dict)
assert not issubclass(dict, Counter)

# namedtuple instances ARE tuples
P = namedtuple('P', 'x y')
p = P(1, 2)
assert isinstance(p, tuple)
assert isinstance(p, P)
assert not isinstance((1, 2), P)
assert issubclass(P, tuple)
assert not issubclass(tuple, P)
assert isinstance(p, (int, tuple))

# a namedtuple whose type name shadows a builtin name is still only a tuple
dict_like = namedtuple('dict', 'a')
assert isinstance(dict_like(1), tuple)
assert not isinstance({}, dict_like)

# bool / int
assert isinstance(True, int)
assert issubclass(bool, int)
assert not issubclass(int, bool)

# datetime.datetime is a datetime.date
now = datetime.datetime(2024, 5, 6, 7, 8, 9)
assert isinstance(now, datetime.date)
assert isinstance(now, datetime.datetime)
assert not isinstance(datetime.date(2024, 5, 6), datetime.datetime)
assert issubclass(datetime.datetime, datetime.date)
assert not issubclass(datetime.date, datetime.datetime)
assert isinstance(datetime.timezone.utc, datetime.tzinfo)

# type() of an instance is subclass-aware too
assert issubclass(type(Counter()), dict)
assert issubclass(type(True), int)
