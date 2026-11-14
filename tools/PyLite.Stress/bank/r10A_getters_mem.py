# deadline: 20000
from operator import itemgetter, attrgetter, methodcaller
N = 1000000
gs = [itemgetter(i) for i in range(N)]
print(len(gs), gs[5]([0, 1, 2, 3, 4, 5, 6]))
