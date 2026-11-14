class B: pass
class A(b := B): pass
print(A.__bases__, b)
