# DeadlineMs: wall-clock time of the whole run, checked on the same cadence as steps and inside long
# native operations. Half a second is far short of the default step budget, so the clock stops the loop.
# limits: DeadlineMs=500
# expect: BudgetExceeded DeadlineMs
while True:
    pass
