# repeat: 2
xs = [[i] for i in range(20000)]
t = 0
for x in xs:
    t += id(x) & 1
import copy
m = {}
for i in range(200):
    copy.deepcopy([[i], {"a": [i]}], m)
print(t)
