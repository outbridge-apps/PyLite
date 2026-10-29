# RegexPerOpTimeoutMs: one regular-expression operation. Catastrophic backtracking ends the run; the
# limit is reported as RegexPerOpTimeout. All regex time together also counts against DeadlineMs.
# limits: RegexPerOpTimeoutMs=50
# expect: BudgetExceeded RegexPerOpTimeout
import re
m = re.match(r'(a+)+$', 'a' * 40 + 'b')
