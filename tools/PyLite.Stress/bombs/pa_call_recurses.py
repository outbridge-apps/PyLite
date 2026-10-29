# deadline: 20000
# allocmb: 1024
class A:
    def __call__(self):
        return self()
try:
    print(A()())
except RecursionError as e:
    print('clean RecursionError')
