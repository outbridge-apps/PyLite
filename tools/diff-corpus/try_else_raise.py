log = []
try:
    try:
        log.append('body')
    except ValueError:
        log.append('caught-by-own')
    else:
        raise ValueError('from-else')
except ValueError:
    log.append('outer')
result = log
