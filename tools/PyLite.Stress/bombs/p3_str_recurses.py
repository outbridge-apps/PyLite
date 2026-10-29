# deadline: 20000
# allocmb: 1024
class A:
    def __str__(self):
        return str(self)
try:
    print(str(A()))
except RecursionError as e:
    print('clean RecursionError')
