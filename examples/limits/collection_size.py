# MaxCollectionItems: the items of any one list, tuple, dict, set or deque, checked before it grows.
# limits: MaxCollectionItems=100000
# expect: BudgetExceeded MaxCollectionItems
xs = list(range(200000))
