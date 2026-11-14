from collections import namedtuple
from typing import NamedTuple, TypedDict
for name in ["list", "int", "dict", "str", "tuple", "date", "Decimal", "Counter", "object", "type", "Pattern", "Match", "Element"]:
    X = namedtuple(name, "a b")
    x = X(1, 2)
    try:
        print(name, type(x) is X, isinstance(x, X), repr(type(x)), x, X._fields, x._asdict(), X._make([3, 4]))
    except Exception as e:
        print(name, type(e).__name__, e)
    T = NamedTuple(name, [("a", int)])
    t = T(5)
    print(" ", type(t) is T, t)
    D = TypedDict(name, {"a": int})
    print(" ", D(a=1), type(D(a=1)) is dict, repr(D))
