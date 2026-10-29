log = []
try:
    try:
        raise ValueError('x')
    except unknown_name:
        log.append('never')
    finally:
        log.append('finally')
except NameError:
    log.append('outer')
result = log
