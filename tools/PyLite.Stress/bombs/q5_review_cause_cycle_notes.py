# deadline: 3000
# allocmb: 1024
e = ValueError()
for i in range(8):
    e.add_note('n' * 1000000)
raise e from e
