# repeat: 2
import typing
t = 0
for k in range(300):
    L = typing.Literal[k, k + 1, "a%d" % k]
    t += (L == typing.Literal[k + 1, k, "a%d" % k])
    U = typing.Optional[typing.List[int]]
    t += len(repr(U))
print(t)
