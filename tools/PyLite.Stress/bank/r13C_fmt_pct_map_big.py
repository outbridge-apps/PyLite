try:
    print(len("%(a)s" * 1000000 % {"a": "x" * 20}))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
