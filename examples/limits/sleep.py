# SleepTotalMs: the total a run may spend in time.sleep, so a script cannot hold a run slot by waiting.
# limits: SleepTotalMs=200
# expect: BudgetExceeded SleepTotalMs
import time
for _ in range(10):
    time.sleep(0.05)
