# MaxAllocBytes: a cumulative charge on what the run allocates (freeing does not refund it), so a growing
# structure stops long before it can press on the host's memory.
# limits: MaxAllocBytes=8388608
# expect: BudgetExceeded MaxAllocBytes
chunks = []
while True:
    chunks.append('x' * 1000)
