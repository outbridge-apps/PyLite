try:
    print(("{0" + ".real" * 100000 + "}").format(1))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
