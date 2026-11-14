import datetime, re, collections, decimal, json, math, functools, itertools, io
from xml.etree import ElementTree as ET
targets = {
    "iter_type": type(iter([])),
    "range_iter": type(iter(range(3))),
    "zip_type": type(zip()),
    "map_type": type(map(len, [])),
    "gen_type": type(x for x in []),
    "dt_type": type(datetime.datetime(2020, 1, 1)),
    "datetime_cls": datetime.datetime,
    "td_cls": datetime.timedelta,
    "pattern_type": type(re.compile("a")),
    "match_type": type(re.match("a", "a")),
    "int": int, "str": str, "list": list, "dict": dict, "type": type,
    "ValueError": ValueError, "Exception": Exception, "BaseException": BaseException,
    "len": len, "print": print, "sorted": sorted,
    "Decimal": decimal.Decimal, "Counter": collections.Counter, "OrderedDict": collections.OrderedDict,
    "deque": collections.deque, "partial": functools.partial, "Element": ET.Element,
    "json_mod": json, "math_mod": math, "re_mod": re, "dt_mod": datetime,
    "math.sqrt": math.sqrt, "json.dumps": json.dumps, "re.compile": re.compile,
    "StringIO": io.StringIO, "chain": itertools.chain, "NoneType": type(None), "bool": bool,
    "dict_keys": type({}.keys()), "func_type": type(lambda: 0), "method_type": type([].append),
    "decimal_ctx": decimal.getcontext(), "utc": datetime.timezone.utc,
}
ok = []
for name, t in targets.items():
    try:
        setattr(t, "zz_probe", [1, 2, 3])
        ok.append(name)
    except Exception as e:
        pass
print("settable:", ok)
