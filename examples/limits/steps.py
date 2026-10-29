# MaxSteps: every statement and expression costs steps, so a loop that never ends runs out of them.
# limits: MaxSteps=1000000
# expect: BudgetExceeded MaxSteps
n = 0
while True:
    n += 1
