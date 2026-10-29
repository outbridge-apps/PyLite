# deadline: 20000
# allocmb: 1024
s = chr(8364) * 8000000
print(len(s.encode('cp866', 'replace')))
