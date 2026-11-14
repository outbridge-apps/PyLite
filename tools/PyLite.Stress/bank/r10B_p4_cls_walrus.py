class B:
    def f(self): return 1
class A(b := B): pass
print(A().f(), b)
