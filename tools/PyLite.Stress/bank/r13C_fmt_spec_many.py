try:
    print(len(format(1, "{" * 1000)))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
try:
    print(len(("{:" + "{}" * 100000 + "}").format(1, *range(100000))))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
