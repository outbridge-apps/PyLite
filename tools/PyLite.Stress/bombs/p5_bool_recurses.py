# deadline: 20000
# allocmb: 1024
class A:
    def __bool__(self):
        return bool(self)
try:
    print(bool(A()))
except RecursionError as e:
    print('clean RecursionError')
