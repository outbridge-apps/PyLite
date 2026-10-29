# MaxCallDepth: nested script calls. Recursion without a base case ends the run here, long before the
# host's own stack is at risk.
# limits: MaxCallDepth=50
# expect: BudgetExceeded MaxCallDepth
def down(n):
    return down(n + 1)

down(0)
