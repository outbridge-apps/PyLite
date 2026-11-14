# deadline: 3000
s = '0' * 8000000
try:
    print('{:{a}{a}{a}{a}{a}{a}{a}{a}{a}{a}}'.format(1, a=s))
except ValueError as e:
    print(e)
