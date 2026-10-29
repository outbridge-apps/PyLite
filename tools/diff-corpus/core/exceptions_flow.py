log = []


class AppError(Exception):
    pass


class NotFound(AppError):
    pass


def step(n):
    try:
        log.append('try %d' % n)
        if n == 1:
            raise NotFound('item %d' % n)
        if n == 2:
            raise ValueError('bad')
        return 'ret %d' % n
    except AppError as e:
        log.append('app %s %s' % (type(e).__name__, e))
        return 'handled'
    else:
        log.append('else %d' % n)
    finally:
        log.append('finally %d' % n)


results = [step(n) for n in range(3) if n != 2]
try:
    step(2)
except ValueError as e:
    results.append(str(e))
try:
    try:
        raise KeyError('k')
    except KeyError as e:
        raise RuntimeError('wrapped') from e
except RuntimeError as e:
    results.append((type(e.__cause__).__name__, e.__cause__.args))
try:
    try:
        raise KeyError('k')
    except KeyError:
        raise RuntimeError('during')
except RuntimeError as e:
    results.append((type(e.__context__).__name__, e.__cause__ is None))
try:
    try:
        raise KeyError('k')
    except KeyError:
        raise RuntimeError('plain') from None
except RuntimeError as e:
    results.append(e.__cause__)
try:
    raise NotFound('x', 42)
except AppError as e:
    results.append((e.args, isinstance(e, Exception), repr(e)))


def loop():
    for i in range(5):
        try:
            if i == 1:
                continue
            if i == 3:
                break
            log.append('body %d' % i)
        finally:
            log.append('fin %d' % i)
    return 'done'


results.append(loop())
result = (results, log)
