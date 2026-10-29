# Adapted from CPython's test suite (Lib/test/test_decimal.py: ContextWithStatement and the
# setcontext/copy parts of ContextAPItests). Python Software Foundation, PSF License Version 2 --
# see conformance/README.md.
# dialect: a context carries prec (at most 28, the System.Decimal backend) and rounding only.
from decimal import Decimal, getcontext, setcontext, localcontext, Context
from decimal import ROUND_DOWN, ROUND_HALF_UP, ROUND_HALF_EVEN

def raises(exc, f):
    try:
        f()
    except exc as e:
        return str(e)
    return None

# ---- test_localcontext ----
orig_ctx = getcontext()
with localcontext() as enter_ctx:
    set_ctx = getcontext()
final_ctx = getcontext()
assert orig_ctx is final_ctx, 'did not restore context correctly'
assert orig_ctx is not set_ctx, 'did not copy the context'
assert set_ctx is enter_ctx, '__enter__ returned wrong context'

# ---- test_localcontextarg ----
orig_ctx = getcontext()
new_ctx = Context(prec=12)
with localcontext(new_ctx) as enter_ctx:
    set_ctx = getcontext()
final_ctx = getcontext()
assert orig_ctx is final_ctx
assert set_ctx.prec == new_ctx.prec
assert new_ctx is not set_ctx
assert set_ctx is enter_ctx

# ---- test_local_context_kwargs_does_not_overwrite_existing_argument ----
ctx = getcontext()
orig_prec = ctx.prec
with localcontext(prec=10) as ctx2:
    assert ctx2.prec == 10
    assert ctx.prec == orig_prec
with localcontext(prec=20) as ctx2:
    assert ctx2.prec == 20
    assert ctx.prec == orig_prec

# ---- test_localcontext_kwargs, the prec/rounding part ----
with localcontext(prec=10, rounding=ROUND_HALF_UP) as c:
    assert c.prec == 10 and c.rounding == ROUND_HALF_UP
assert raises(TypeError, lambda: localcontext(precision=10)) is not None
assert raises(ValueError, lambda: localcontext(prec=0)) is not None

# ---- the arithmetic inside uses the local context; the old one comes back ----
with localcontext() as c:
    c.prec = 5
    assert str(Decimal(1) / Decimal(3)) == '0.33333'
assert str(Decimal(1) / Decimal(3)) == '0.3333333333333333333333333333'
with localcontext(rounding=ROUND_DOWN):
    assert str(Decimal('2.675').quantize(Decimal('0.01'))) == '2.67'
assert str(Decimal('2.675').quantize(Decimal('0.01'))) == '2.68'

def boom():
    with localcontext() as c:
        c.prec = 3
        raise ValueError('inside')
assert raises(ValueError, boom) == 'inside'
assert getcontext().prec == 28 and getcontext().rounding == ROUND_HALF_EVEN

with localcontext() as a:
    a.prec = 10
    with localcontext() as b:
        assert b.prec == 10
        b.prec = 4
        assert getcontext().prec == 4
    assert getcontext().prec == 10
assert getcontext().prec == 28

# ---- setcontext, Context, copy ----
saved = getcontext()
mine = Context(prec=6)
setcontext(mine)
assert getcontext() is mine and str(Decimal(2) / Decimal(3)) == '0.666667'
setcontext(saved)
assert getcontext() is saved
dup = saved.copy()
assert dup is not saved and dup.prec == saved.prec and dup.rounding == saved.rounding
dup.prec = 7
assert saved.prec == 28
assert isinstance(getcontext(), Context)
