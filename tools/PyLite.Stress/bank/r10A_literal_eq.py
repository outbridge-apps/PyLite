from typing import Literal, Union, Optional
import enum
class E(enum.Enum):
    A = 1
    B = 2
class IE(enum.IntEnum):
    X = 1
L = Literal
cases = [
    (L[1, 2], L[2, 1]), (L[1], L[True]), (L[0], L[False]), (L[1, True], L[True, 1]), (L[1, 1], L[1]),
    (L["a"], L[b"a"]), (L[None], L[None]), (L[None, 1], L[1, None]), (L[2**64], L[2**64]), (L[2**64], L[2**64 + 1]),
    (L[-1], L[-2]), (L[E.A], L[E.A]), (L[E.A, 1], L[1, E.A]), (L[IE.X], L[1]), (L[1, "1"], L["1", 1]),
    (L[1, 2, 3], L[1, 2]), (L[1, 2], L[1, 2, 2]), (L["", b""], L[b"", ""]), (L[(1, 2)] if False else L[1], L[1]),
    (L[1, L[2, 3]], L[1, 2, 3]), (L[2**61 - 1], L[0]), (L[-1], L[-2]), (L[hash("x")], L["x"]),
    (L[True, False], L[False, True]), (L[True], L[True, True]), (L["a", "b"], L["a", "c"]),
]
for a, b in cases:
    try:
        print(repr(a), repr(b), a == b, b == a, a != b, hash(a) == hash(b) if a == b else "-")
    except Exception as e:
        print(type(e).__name__, e)
print(L[1, 2] in {L[2, 1]: 1})
big = L[tuple(range(3000))]
big2 = L[tuple(range(2999, -1, -1))]
print(big == big2, big == L[tuple(range(2999))])
try:
    print(Union[()])
except Exception as e:
    print(type(e).__name__, e)
try:
    print(Union[int])
    print(Union[int, str] == Union[str, int], (int | str) == (str | int), (int | None) == Optional[int])
    print(hash(int | str) == hash(str | int))
except Exception as e:
    print(type(e).__name__, e)
class A: pass
class B: pass
print((A | B | None) == (None | B | A), (A | B) == (A | None), (A | B) == (A | B | None))
