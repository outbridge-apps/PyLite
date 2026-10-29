# MaxStrChars: the length of any one string or bytes value, checked before it is built.
# limits: MaxStrChars=1000000
# expect: BudgetExceeded MaxStrChars
s = 'ab' * 600000
