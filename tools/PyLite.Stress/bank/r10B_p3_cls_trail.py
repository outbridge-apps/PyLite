class B: pass
class A(B,): pass
print(A.__mro__)
