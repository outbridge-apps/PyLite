# deadline: 20000
# allocmb: 1024
class A:
    def __iter__(self):
        return self
    def __next__(self):
        return 1
print(len(list(A())))
