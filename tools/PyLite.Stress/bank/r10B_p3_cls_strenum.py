from enum import Enum
class X(str, Enum):
    A = "a"
print(X.A, X("a"))
