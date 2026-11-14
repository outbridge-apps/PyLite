class B:
    def f(self): return 1
class A((B)): pass
print(A().f())
