class B: pass
class A((B)): pass
print(A.__bases__)
