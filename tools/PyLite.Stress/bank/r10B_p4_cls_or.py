class B:
    def f(self): return 1
class A(None or B): pass
print(A().f())
