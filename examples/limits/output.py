# MaxOutputBytes: everything the run prints, two bytes a character (UTF-16).
# limits: MaxOutputBytes=10000
# expect: BudgetExceeded MaxOutputBytes
for i in range(10000):
    print('line', i)
