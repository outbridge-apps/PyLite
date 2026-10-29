# MaxIntBits: the size of an integer result, checked before the arithmetic runs, so an exponent tower
# cannot take minutes of CPU.
# limits: MaxIntBits=10000
# expect: BudgetExceeded MaxIntBits
n = 3 ** 20000
