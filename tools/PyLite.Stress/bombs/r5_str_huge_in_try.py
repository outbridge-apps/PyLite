# deadline: 2000
for i in range(3):
    try:
        print(len(str(2 ** 900000)))
    except ValueError as e:
        print(str(e)[:20])
