# Adapted from CPython's test suite (Lib/test/test_exceptions.py: ExceptionTests.testRaise*,
# test_try_finally, test_nested_try, Lib/test/test_raise.py TestCause/TestContext, test_sys StopIteration).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.

# ---- except forms ----
def classify(fn):
    try:
        fn()
    except ZeroDivisionError:
        return 'zde'
    except (KeyError, IndexError) as e:
        return 'lookup:' + type(e).__name__
    except ValueError as e:
        return 'value:' + str(e)
    except Exception as e:
        return 'exc:' + type(e).__name__
    return 'ok'

assert classify(lambda: 1 / 0) == 'zde'
assert classify(lambda: {}['k']) == 'lookup:KeyError'
assert classify(lambda: [][0]) == 'lookup:IndexError'
assert classify(lambda: int('x')) == "value:invalid literal for int() with base 10: 'x'"
assert classify(lambda: None.x) == 'exc:AttributeError'
assert classify(lambda: 1) == 'ok'

# catch-by-base and hierarchy
assert issubclass(ZeroDivisionError, ArithmeticError)
assert issubclass(OverflowError, ArithmeticError)
assert issubclass(KeyError, LookupError) and issubclass(IndexError, LookupError)
assert issubclass(UnboundLocalError, NameError)
assert issubclass(NotImplementedError, RuntimeError)
assert issubclass(Exception, BaseException) and not issubclass(BaseException, Exception)
assert issubclass(StopIteration, Exception)
try:
    {}['x']
except LookupError:
    pass
try:
    raise OverflowError('big')
except ArithmeticError as e:
    assert str(e) == 'big'

# bare except catches everything
try:
    raise KeyError('k')
except:
    caught = True
assert caught

# ---- args and str ----
try:
    raise ValueError('a', 1)
except ValueError as e:
    assert e.args == ('a', 1)
    assert str(e) == "('a', 1)"
    assert repr(e) == "ValueError('a', 1)"
try:
    raise ValueError()
except ValueError as e:
    assert e.args == () and str(e) == '' and repr(e) == 'ValueError()'
try:
    raise KeyError('missing')
except KeyError as e:
    assert str(e) == "'missing'"              # KeyError str is the repr of the key
    assert e.args == ('missing',)
try:
    raise TypeError                          # class, not instance
except TypeError as e:
    assert str(e) == ''

# the bound name is cleared after the handler
try:
    raise ValueError('x')
except ValueError as bound:
    pass
try:
    bound
    assert False
except NameError:
    pass

# ---- else / finally ordering ----
log = []
def run(fail):
    try:
        log.append('try')
        if fail:
            raise ValueError('f')
    except ValueError:
        log.append('except')
    else:
        log.append('else')
    finally:
        log.append('finally')
    log.append('after')
run(False)
assert log == ['try', 'else', 'finally', 'after']
log.clear()
run(True)
assert log == ['try', 'except', 'finally', 'after']

# finally runs on return and its value does not leak; a return in finally overrides
def fin_ret():
    try:
        return 'try'
    finally:
        log.append('fin')
log.clear()
assert fin_ret() == 'try' and log == ['fin']
def fin_override():
    try:
        return 'try'
    finally:
        return 'finally'
assert fin_override() == 'finally'
def fin_swallow():
    try:
        raise ValueError('lost')
    finally:
        return 'swallowed'                # return in finally discards the exception
assert fin_swallow() == 'swallowed'

# finally runs on break/continue
out = []
for i in range(3):
    try:
        if i == 1:
            continue
        if i == 2:
            break
        out.append(i)
    finally:
        out.append('f' + str(i))
assert out == [0, 'f0', 'f1', 'f2']

# ---- nested try and re-raise ----
def reraise():
    try:
        try:
            raise ValueError('inner')
        except ValueError:
            raise                           # bare raise re-raises the active exception
    except ValueError as e:
        return 'outer:' + str(e)
assert reraise() == 'outer:inner'

def bare_raise_without_handler():
    try:
        raise                               # nothing is being handled here
    except RuntimeError as e:
        return str(e)
assert bare_raise_without_handler() == 'No active exception to re-raise'

def bare_raise_after_handler():
    try:
        raise ValueError('done')
    except ValueError:
        pass
    try:
        raise                               # the handler has finished, the exception is no longer active
    except RuntimeError as e:
        return str(e)
assert bare_raise_after_handler() == 'No active exception to re-raise'

def replace():
    try:
        try:
            raise KeyError('k')
        except KeyError:
            raise ValueError('replaced')
    except ValueError as e:
        return str(e)
assert replace() == 'replaced'

def handler_error_propagates():
    try:
        try:
            raise ValueError('first')
        except ValueError:
            1 / 0
    except ZeroDivisionError:
        return 'zde-from-handler'
assert handler_error_propagates() == 'zde-from-handler'

# exception in else clause is not caught by the same try's handlers
def else_raises():
    try:
        try:
            pass
        except ValueError:
            return 'wrong'
        else:
            raise ValueError('from-else')
    except ValueError:
        return 'outer'
assert else_raises() == 'outer'

# ---- raise from / context ----
try:
    try:
        raise KeyError('cause')
    except KeyError as k:
        raise ValueError('effect') from k
except ValueError as v:
    assert isinstance(v.__cause__, KeyError)
    assert v.__cause__.args == ('cause',)
try:
    try:
        raise KeyError('ctx')
    except KeyError:
        raise ValueError('implicit')
except ValueError as v:
    assert isinstance(v.__context__, KeyError)
    assert v.__cause__ is None
try:
    raise ValueError('bare')
except ValueError as v:
    assert v.__cause__ is None and v.__context__ is None
try:
    raise ValueError('no-cause') from None
except ValueError as v:
    assert v.__cause__ is None

# raise a non-exception is a TypeError
try:
    raise 42
    assert False
except TypeError:
    pass

# ---- custom exceptions ----
class AppError(Exception):
    pass
class NotFound(AppError):
    pass
class Conflict(AppError):
    pass

def raise_nf():
    raise NotFound('id=7')
try:
    raise_nf()
except AppError as e:
    assert isinstance(e, NotFound) and str(e) == 'id=7' and type(e).__name__ == 'NotFound'
try:
    raise Conflict
except NotFound:
    assert False
except Conflict:
    pass
assert issubclass(NotFound, AppError) and not issubclass(NotFound, Conflict)
e = NotFound('a', 'b')
assert e.args == ('a', 'b') and isinstance(e, Exception)
try:
    raise e
except (NotFound, Conflict) as caught:
    assert caught is e
class LookupFail(LookupError):
    pass
try:
    raise LookupFail('x')
except LookupError:
    pass

# ---- exceptions from builtins carry the right type ----
def expect(fn, exc):
    try:
        fn()
    except exc:
        return True
    except Exception:
        return False
    return False
assert expect(lambda: int('abc'), ValueError)
assert expect(lambda: [1, 2].index(3), ValueError)
assert expect(lambda: 'abc'.index('z'), ValueError)
assert expect(lambda: {}.pop('k'), KeyError)
assert expect(lambda: [].pop(), IndexError)
assert expect(lambda: (1, 2)[5], IndexError)
assert expect(lambda: 'abc'[10], IndexError)
assert expect(lambda: len(5), TypeError)
assert expect(lambda: {[]: 1}, TypeError)              # unhashable key
assert expect(lambda: {}[[]], TypeError)
assert expect(lambda: undefined_name_xyz, NameError)
assert expect(lambda: next(iter([])), StopIteration)
assert expect(lambda: 10 ** 10000.0, OverflowError)
assert expect(lambda: int(float('inf')), OverflowError)
assert expect(lambda: len(), TypeError)
assert expect(lambda: chr(-1), ValueError)
assert expect(lambda: [1][1.0], TypeError)
assert expect(lambda: 'x' * 'y', TypeError)
assert expect(lambda: None(), TypeError)
assert expect(lambda: {}.no_such(), AttributeError)

def local_before_assign():
    v = v + 1
    return v
v = 1
assert expect(local_before_assign, UnboundLocalError)
assert expect(local_before_assign, NameError)           # UnboundLocalError is a NameError

# NotImplementedError is raised manually and caught as RuntimeError
def abstract():
    raise NotImplementedError('override me')
assert expect(abstract, NotImplementedError) and expect(abstract, RuntimeError)

# StopIteration carries a value
try:
    raise StopIteration(99)
except StopIteration as si:
    assert si.args == (99,)

# ---- exception objects are ordinary values ----
errs = [ValueError('a'), KeyError('b')]
assert [type(x).__name__ for x in errs] == ['ValueError', 'KeyError']
assert str(errs[0]) == 'a'
by_type = {type(x).__name__: x for x in errs}
assert 'KeyError' in by_type

# raising inside a comprehension propagates
try:
    [1 / (i - 1) for i in range(3)]
    assert False
except ZeroDivisionError:
    pass

# ---- try inside loops, exceptions in nested calls unwind through frames ----
def level3():
    raise ValueError('deep')
def level2():
    level3()
def level1():
    try:
        level2()
    except ValueError as e:
        return 'caught ' + str(e)
assert level1() == 'caught deep'

results = []
for item in ['1', 'x', '3']:
    try:
        results.append(int(item))
    except ValueError:
        results.append(None)
assert results == [1, None, 3]
