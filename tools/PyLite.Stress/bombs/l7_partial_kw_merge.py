# deadline: 20000
# allocmb: 1024
import functools
d = {('k' + str(i)): i for i in range(200000)}
e = {('k' + str(i)): -i for i in range(200000)}
q = functools.partial(functools.partial(dict, **d), **e)
print(len(q()))
