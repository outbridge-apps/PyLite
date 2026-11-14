# deadline: 3000
s = '0' * 8000000
try:
    print(f"{1:{s}{s}{s}{s}{s}{s}{s}{s}{s}{s}}")
except ValueError as e:
    print(e)
