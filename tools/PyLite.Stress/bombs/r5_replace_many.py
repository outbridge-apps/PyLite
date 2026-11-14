# deadline: 3000
import dataclasses
C = dataclasses.make_dataclass('C', ['f' + str(i) for i in range(40000)])
o = C(*range(40000))
p = dataclasses.replace(o, **{'f' + str(i): 0 for i in range(40000)})
print(p.f0)
