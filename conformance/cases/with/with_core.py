# Adapted from CPython's test suite (Lib/test/test_with.py: FailureTestCase, NonexceptionalTestCase,
# NestedNonexceptionalTestCase, ExceptionalTestCase, NonLocalFlowControlTestCase,
# AssignmentTargetTestCase, ExitSwallowsExceptionTestCase, NestedWith).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# The @contextmanager-based mocks are rewritten as classes with __enter__/__exit__ (the dialect has no
# generators); the cases that test generator machinery itself (StopIteration/GeneratorExit raised in
# the body of a generator manager, testWithYield) are left out. The traceback argument of __exit__ is
# None (dialect: there are no traceback objects).

class MockResource:
    def __init__(self):
        self.yielded = False
        self.stopped = False

class MockContextManager:
    def __init__(self):
        self.enter_called = False
        self.exit_called = False
        self.exit_args = None
        self.resource = MockResource()
    def __enter__(self):
        self.enter_called = True
        self.resource.yielded = True
        return self.resource
    def __exit__(self, t, v, tb):
        self.exit_called = True
        self.exit_args = (t, v, tb)
        self.resource.stopped = True
        return False

def raises(exc, f):
    try:
        f()
    except exc as e:
        return e
    return None

# ================= FailureTestCase =================
def name_error():
    with foo_never_defined:
        pass
assert raises(NameError, name_error) is not None

class LacksEnter:
    def __exit__(self, type, value, traceback):
        pass

class LacksEnterAndExit:
    pass

class LacksExit:
    def __enter__(self):
        pass

def lacks_enter():
    with LacksEnter():
        pass
def lacks_both():
    with LacksEnterAndExit():
        pass
def lacks_exit():
    with LacksExit():
        pass
e = raises(TypeError, lacks_enter)
assert e is not None and 'the context manager' in str(e), e
e = raises(TypeError, lacks_both)
assert e is not None and 'the context manager' in str(e), e
e = raises(TypeError, lacks_exit)
assert e is not None and 'the context manager' in str(e) and '__exit__' in str(e), e

class EnterThrows:
    def __init__(self):
        self.exit_called = False
    def __enter__(self):
        raise RuntimeError("Enter threw")
    def __exit__(self, *args):
        self.exit_called = True

ct = EnterThrows()
foo = None
def enter_throws():
    global foo
    with ct as foo:
        pass
assert raises(RuntimeError, enter_throws) is not None
assert foo is None
assert not ct.exit_called

class ExitThrows:
    def __enter__(self):
        return
    def __exit__(self, *args):
        raise RuntimeError(42)

def exit_throws():
    with ExitThrows():
        pass
e = raises(RuntimeError, exit_throws)
assert e is not None and e.args == (42,)

# ================= NonexceptionalTestCase =================
def assert_in_with_manager_invariants(mock):
    assert mock.enter_called
    assert not mock.exit_called

def assert_after_with_manager_invariants(mock, exit_args):
    assert mock.enter_called
    assert mock.exit_called
    assert mock.exit_args == exit_args

def assert_after_with_manager_invariants_no_error(mock):
    assert_after_with_manager_invariants(mock, (None, None, None))

def assert_in_with_resource_invariants(resource):
    assert resource.yielded
    assert not resource.stopped

def assert_after_with_resource_invariants(resource):
    assert resource.yielded
    assert resource.stopped

# testInlineGeneratorSyntax
with MockContextManager():
    pass

# testUnboundGenerator
mock = MockContextManager()
with mock:
    pass
assert_after_with_manager_invariants_no_error(mock)

# testInlineGeneratorBoundSyntax
with MockContextManager() as foo:
    assert_in_with_resource_invariants(foo)
assert_after_with_resource_invariants(foo)

# testInlineGeneratorBoundToExistingVariable
foo = None
with MockContextManager() as foo:
    assert_in_with_resource_invariants(foo)
assert_after_with_resource_invariants(foo)

# testInlineGeneratorBoundToDottedVariable (dialect: the attribute is a declared field)
class Holder:
    foo: object = None
holder = Holder()
with MockContextManager() as holder.foo:
    assert_in_with_resource_invariants(holder.foo)
assert_after_with_resource_invariants(holder.foo)

# testBoundGenerator
mock = MockContextManager()
with mock as foo:
    assert_in_with_resource_invariants(foo)
    assert_in_with_manager_invariants(mock)
assert_after_with_resource_invariants(foo)
assert_after_with_manager_invariants_no_error(mock)

# testNestedSingleStatements
mock_a = MockContextManager()
with mock_a as foo:
    mock_b = MockContextManager()
    with mock_b as bar:
        assert_in_with_manager_invariants(mock_a)
        assert_in_with_manager_invariants(mock_b)
        assert_in_with_resource_invariants(foo)
        assert_in_with_resource_invariants(bar)
    assert_after_with_manager_invariants_no_error(mock_b)
    assert_after_with_resource_invariants(bar)
    assert_in_with_resource_invariants(foo)
    assert_in_with_manager_invariants(mock_a)
assert_after_with_resource_invariants(foo)
assert_after_with_manager_invariants_no_error(mock_a)

# ================= NestedNonexceptionalTestCase (Nested -> several items) =================
m1 = MockContextManager()
m2 = MockContextManager()
with m1 as a, m2 as b:
    assert a is m1.resource and b is m2.resource
    assert_in_with_manager_invariants(m1)
    assert_in_with_manager_invariants(m2)
assert_after_with_manager_invariants_no_error(m1)
assert_after_with_manager_invariants_no_error(m2)

class ListManager:
    def __init__(self, value):
        self.value = value
    def __enter__(self):
        return self.value
    def __exit__(self, *args):
        return False

# testSingleArgBoundToSingleElementParenthesizedList
with ListManager([1]) as (foo):
    assert foo == [1]
# testSingleArgBoundToMultipleElementTupleError
def unpack_too_many():
    with ListManager([1]) as (foo, bar):
        pass
assert raises(ValueError, unpack_too_many) is not None

# ================= ExceptionalTestCase =================
def raise_test_exception():
    raise RuntimeError('test exception')

def assert_after_with_manager_invariants_with_error(mock, exc_type=None):
    assert mock.enter_called
    assert mock.exit_called
    if exc_type is None:
        assert mock.exit_args[1] is not None
        exc_type = type(mock.exit_args[1])
    assert mock.exit_args[0] is exc_type, mock.exit_args
    assert isinstance(mock.exit_args[1], exc_type)
    assert mock.exit_args[2] is None   # dialect: no traceback objects

def assert_after_with_resource_invariants_with_error(resource):
    assert resource.yielded
    assert resource.stopped

# testSingleResource
cm = MockContextManager()
def single_resource():
    with cm as self_resource:
        assert_in_with_manager_invariants(cm)
        assert_in_with_resource_invariants(self_resource)
        raise_test_exception()
assert raises(RuntimeError, single_resource) is not None
assert_after_with_manager_invariants_with_error(cm)

# testExceptionNormalized
cm = MockContextManager()
def normalized():
    with cm as self_resource:
        1 / 0
assert raises(ZeroDivisionError, normalized) is not None
assert_after_with_manager_invariants_with_error(cm, ZeroDivisionError)

# testNestedSingleStatements
mock_a = MockContextManager()
mock_b = MockContextManager()
def nested_single():
    with mock_a as foo:
        with mock_b as bar:
            assert_in_with_manager_invariants(mock_a)
            assert_in_with_manager_invariants(mock_b)
            raise_test_exception()
assert raises(RuntimeError, nested_single) is not None
assert_after_with_manager_invariants_with_error(mock_a)
assert_after_with_manager_invariants_with_error(mock_b)

# testMultipleResourcesInSingleStatement
cm_a = MockContextManager()
cm_b = MockContextManager()
def multiple_single():
    with cm_a as a, cm_b as b:
        raise_test_exception()
assert raises(RuntimeError, multiple_single) is not None
assert_after_with_manager_invariants_with_error(cm_a)
assert_after_with_manager_invariants_with_error(cm_b)

# testNestedExceptionBeforeInnerStatement
mock_a = MockContextManager()
mock_b = MockContextManager()
state = {'bar': None}
def before_inner():
    with mock_a as foo:
        raise_test_exception()
        with mock_b as bar:
            state['bar'] = bar
assert raises(RuntimeError, before_inner) is not None
assert_after_with_manager_invariants_with_error(mock_a)
assert not mock_b.enter_called and not mock_b.exit_called
assert state['bar'] is None

# testNestedExceptionAfterInnerStatement
mock_a = MockContextManager()
mock_b = MockContextManager()
def after_inner():
    with mock_a as foo:
        with mock_b as bar:
            pass
        raise_test_exception()
assert raises(RuntimeError, after_inner) is not None
assert_after_with_manager_invariants_with_error(mock_a)
assert_after_with_manager_invariants_no_error(mock_b)

# testErrorsInBool
class BoolRaises:
    def __init__(self, exc):
        self.exc = exc
    def __bool__(self):
        raise self.exc

class ExitReturns:
    def __init__(self, result):
        self.result = result
    def __enter__(self):
        return self
    def __exit__(self, *args):
        return self.result

def truthy():
    with ExitReturns(BoolRaises(KeyError('bool'))):
        raise ZeroDivisionError('body')
e = raises(KeyError, truthy)
assert e is not None and e.args == ('bool',), e
def truthy_ok():
    with ExitReturns(True):
        raise ZeroDivisionError('body')
truthy_ok()

# ================= NonLocalFlowControlTestCase =================
counter = 0
while True:
    counter += 1
    with MockContextManager():
        counter += 10
        break
    counter += 100   # Not reached
assert counter == 11

counter = 0
while True:
    counter += 1
    if counter > 2:
        break
    with MockContextManager():
        counter += 10
        continue
    counter += 100   # Not reached
assert counter == 12

def with_return():
    counter = 0
    while True:
        counter += 1
        with MockContextManager():
            counter += 10
            return counter
        counter += 100   # Not reached
assert with_return() == 11

counter = 0
try:
    counter += 1
    with MockContextManager():
        counter += 10
        raise RuntimeError
    counter += 100   # Not reached
except RuntimeError:
    assert counter == 11
else:
    assert False, "Didn't raise RuntimeError"

# ================= AssignmentTargetTestCase =================
targets = {1: [0, 1, 2]}
with MockContextManager() as targets[1][0]:
    assert list(targets.keys()) == [1]
    assert isinstance(targets[1][0], MockResource)
with MockContextManager() as list(targets.values())[0][1]:
    assert list(targets.keys()) == [1]
    assert isinstance(targets[1][1], MockResource)
with MockContextManager() as targets[2]:
    keys = list(targets.keys())
    keys.sort()
    assert keys == [1, 2]

class Triple:
    def __enter__(self):
        return 1, 2, 3
    def __exit__(self, t, v, tb):
        pass

targets = {1: [0, 1, 2]}
with Triple() as (targets[1][0], targets[1][1], targets[1][2]):
    assert targets == {1: [1, 2, 3]}
with Triple() as (list(targets.values())[0][2], list(targets.values())[0][1], list(targets.values())[0][0]):
    assert targets == {1: [3, 2, 1]}
with Triple() as (targets[1], targets[2], targets[3]):
    assert targets == {1: 1, 2: 2, 3: 3}

class Blah:
    one: int = 0
    two: int = 0
    three: int = 0
blah = Blah()
with Triple() as (blah.one, blah.two, blah.three):
    assert blah.one == 1
    assert blah.two == 2
    assert blah.three == 3

# testWithExtendedTargets (nullcontext -> a class)
with ListManager(range(1, 5)) as (a, *b, c):
    assert a == 1
    assert b == [2, 3]
    assert c == 4

# ================= ExitSwallowsExceptionTestCase =================
class AfricanSwallow:
    def __enter__(self):
        pass
    def __exit__(self, t, v, tb):
        return True

try:
    with AfricanSwallow():
        1 / 0
except ZeroDivisionError:
    assert False, "ZeroDivisionError should have been swallowed"

class EuropeanSwallow:
    def __enter__(self):
        pass
    def __exit__(self, t, v, tb):
        return False

try:
    with EuropeanSwallow():
        1 / 0
except ZeroDivisionError:
    pass
else:
    assert False, "ZeroDivisionError should have been raised"

# ================= NestedWith =================
class Dummy:
    def __init__(self, value=None, gobble=False):
        if value is None:
            value = self
        self.value = value
        self.gobble = gobble
        self.enter_called = False
        self.exit_called = False
        self.exc_info = None
    def __enter__(self):
        self.enter_called = True
        return self.value
    def __exit__(self, *exc_info):
        self.exit_called = True
        self.exc_info = exc_info
        if self.gobble:
            return True

class InitRaises:
    def __init__(self):
        raise RuntimeError()

class EnterRaises:
    def __enter__(self):
        raise RuntimeError()
    def __exit__(self, *exc_info):
        pass

class ExitRaises:
    def __enter__(self):
        pass
    def __exit__(self, *exc_info):
        raise RuntimeError()

# testNoExceptions
with Dummy() as a, Dummy() as b:
    assert a.enter_called
    assert b.enter_called
assert a.exit_called
assert b.exit_called

# testExceptionInExprList
a = None
try:
    with Dummy() as a, InitRaises():
        pass
except:
    pass
assert a.enter_called
assert a.exit_called

# testExceptionInEnter
try:
    with Dummy() as a, EnterRaises():
        assert False, 'body of bad with executed'
except RuntimeError:
    pass
else:
    assert False, 'RuntimeError not reraised'
assert a.enter_called
assert a.exit_called

# testExceptionInExit
body_executed = False
with Dummy(gobble=True) as a, ExitRaises():
    body_executed = True
assert a.enter_called
assert a.exit_called
assert body_executed
assert a.exc_info[0] is not None

# testEnterReturnsTuple
with Dummy(value=(1, 2)) as (a1, a2), \
     Dummy(value=(10, 20)) as (b1, b2):
    assert 1 == a1
    assert 2 == a2
    assert 10 == b1
    assert 20 == b2
