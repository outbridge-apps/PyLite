from string import Template
t = Template("$a" * 1000000)
try:
    print(len(t.substitute(a="xxxxxxxxxxxxxxxxxxxx")))
except Exception as e:
    print(type(e).__name__, str(e)[:80])
