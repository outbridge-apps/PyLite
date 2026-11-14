import string
try:
    print(len(list(string.Formatter().parse("{" * 100000 + "}" * 100000))))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
