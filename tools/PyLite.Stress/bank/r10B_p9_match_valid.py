def m(v):
    match v:
        case 1 as one:
            return ("one", one)
        case (2 | 3) as t if t > 2:
            return ("three", t)
        case [4 as a, 5 as b]:
            return ("ab", a, b)
        case {"k": 6 as kk}:
            return ("k", kk)
        case (7 as s) | (8 as s):
            return ("s", s)
        case str() as match:
            return ("str", match)
        case [9, *rest] as whole:
            return ("rest", rest, whole)
        case _ as case:
            return ("other", case)
print([m(x) for x in [1, 3, 2, [4, 5], {"k": 6}, 8, "z", [9, 1], 0]])
