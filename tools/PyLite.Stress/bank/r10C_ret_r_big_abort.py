# repeat: 2
# deadline: 400
import collections
P = collections.namedtuple("P", "a b")
big = []
while True:
    big.append(P(len(big), "x" * 20))
    big[-1].count(1)
