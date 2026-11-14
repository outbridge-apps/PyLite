# deadline: 3000
import unicodedata
s = 'a' + '̖́' * 7999999
try:
    print(len(unicodedata.normalize('NFD', s)))
except ValueError as e:
    print(e)
