# deadline: 3000
# allocmb: 256
t = KeyError
for i in range(700000):
    t = (t,)
try:
    try:
        raise KeyError('k')
    except t:
        pass
except TypeError as x:
    print('clean', type(x).__name__)
