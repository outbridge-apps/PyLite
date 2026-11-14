# deadline: 20000
N = 1000000
gs = [(lambda o, i=i: o[i]) for i in range(N)]
print(len(gs), gs[5]([0, 1, 2, 3, 4, 5, 6]))
