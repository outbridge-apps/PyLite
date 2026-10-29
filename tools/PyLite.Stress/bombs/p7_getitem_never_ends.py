# deadline: 20000
# allocmb: 1024
class A:
    def __getitem__(self, i):
        return i
print(len(list(A())))
