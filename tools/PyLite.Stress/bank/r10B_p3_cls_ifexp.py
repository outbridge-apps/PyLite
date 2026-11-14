class B: pass
class C: pass
class A(B if 0 else C): pass
print(A.__bases__)
