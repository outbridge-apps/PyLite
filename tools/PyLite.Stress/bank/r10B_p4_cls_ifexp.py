class B:
    def f(self): return 1
class C:
    def f(self): return 2
class A(B if 0 else C): pass
print(A().f())
