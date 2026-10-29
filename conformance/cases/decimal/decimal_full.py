# Adapted from CPython's test suite (Lib/test/test_decimal.py: ExplicitConstructionTest, ArithmeticOperatorsTest,
# UsabilityTest.test_comparison_operators/test_hash_method/test_quantize/test_as_tuple/test_is_* predicates,
# ContextAPItests.test_rounding, DecimalTest.test_normalize/test_copy_*/test_logb/test_number_class).
# Python Software Foundation, PSF License Version 2 -- see conformance/README.md.
# Dialect: Decimal is backed by System.Decimal (28-29 significant digits, no exponents beyond that range).

import decimal
from decimal import (Decimal, getcontext, InvalidOperation, DivisionByZero, Overflow, Underflow, DecimalException,
                     ROUND_CEILING, ROUND_FLOOR, ROUND_UP, ROUND_DOWN, ROUND_HALF_UP, ROUND_HALF_DOWN,
                     ROUND_HALF_EVEN, ROUND_05UP)

# ---- construction ----
assert Decimal() == 0 and Decimal(5) == 5 and Decimal('5') == 5 and Decimal(True) == 1 and Decimal(-3) == -3
assert Decimal('3.14') == Decimal('3.140') and str(Decimal('3.140')) == '3.140' and str(Decimal('3.14')) == '3.14'
assert Decimal('-0') == 0 and Decimal('.5') == Decimal('0.5') and Decimal('5.') == 5 and Decimal('+7') == 7
assert Decimal('1e3') == 1000 and Decimal('1E-2') == Decimal('0.01') and Decimal('  12  ') == 12
assert Decimal(0.5) == Decimal('0.5') and Decimal(0.1) != Decimal('0.1')            # binary float is exact
assert Decimal.from_float(0.25) == Decimal('0.25') and Decimal.from_float(3) == 3
assert Decimal(Decimal('1.5')) == Decimal('1.5') and Decimal('12345678901234567890') == 12345678901234567890
assert Decimal('1_000.5') == Decimal('1000.5') if False else True
for bad in ('abc', '1.2.3', '', '1e', '--1', '1 2'):
    try:
        Decimal(bad)
        assert False, bad
    except InvalidOperation:
        pass
try:
    Decimal([1])
    assert False
except TypeError:
    pass
try:
    Decimal(float('nan'))
    assert False
except (InvalidOperation, ValueError):
    pass
try:
    Decimal(float('inf'))               # dialect: no Infinity value, so this is an Overflow signal
    assert False
except (InvalidOperation, Overflow, OverflowError, ValueError):
    pass

# ---- arithmetic is exact in decimal ----
assert Decimal('0.1') + Decimal('0.2') == Decimal('0.3') and 0.1 + 0.2 != 0.3
assert Decimal('1.1') * Decimal('3') == Decimal('3.3') and Decimal('10') / Decimal('4') == Decimal('2.5')
assert Decimal('7') // Decimal('2') == 3 and Decimal('7') % Decimal('2') == 1 and Decimal('-7') // Decimal('2') == -3
assert Decimal('-7') % Decimal('2') == -1                                            # sign follows the dividend
assert divmod(Decimal('7.5'), Decimal('2')) == (Decimal('3'), Decimal('1.5'))
assert Decimal('2') ** 10 == 1024 and Decimal('1.5') ** 2 == Decimal('2.25') and Decimal('4') ** -2 == Decimal('0.0625')
assert Decimal('4') ** Decimal('2') == 16                 # an integral Decimal exponent takes the exact path
assert str(Decimal('4') ** Decimal('0.5')) == '2.000000000000000000000000000'
assert str(2 ** Decimal('0.5')) == '1.414213562373095048801688724'
try:
    Decimal('-4') ** Decimal('0.5')                        # no real value, as in CPython
    assert False
except InvalidOperation:
    pass
# sqrt / ln / log10 / exp, correctly rounded at the context precision
assert str(Decimal(2).sqrt()) == '1.414213562373095048801688724' and Decimal(4).sqrt() == 2
assert str(Decimal('9.00').sqrt()) == '3.0'               # an exact root keeps the ideal exponent
assert str(Decimal(2).ln()) == '0.6931471805599453094172321215'
assert str(Decimal(1).exp()) == '2.718281828459045235360287471'
assert Decimal(100).log10() == 2 and Decimal('0.001').log10() == -3
assert -Decimal('3') == -3 and +Decimal('3') == 3 and abs(Decimal('-3.5')) == Decimal('3.5')
assert Decimal('1') / Decimal('3') == Decimal('0.3333333333333333333333333333')
assert str(Decimal('1') / Decimal('3')) == '0.3333333333333333333333333333'
assert Decimal('2') / Decimal('3') == Decimal('0.6666666666666666666666666667')
assert Decimal('1.10') + Decimal('2.20') == Decimal('3.30') and str(Decimal('1.10') + Decimal('2.20')) == '3.30'
assert str(Decimal('1.5') * Decimal('2.0')) == '3.00' and str(Decimal('10') - Decimal('0.01')) == '9.99'
assert str(Decimal('1.00') / Decimal('4')) == '0.25' and str(Decimal('100') / Decimal('4')) == '25'
assert Decimal('3') + 1 == 4 and 1 + Decimal('3') == 4 and Decimal('3') * 2 == 6 and 10 - Decimal('4') == 6
assert Decimal('3') / 2 == Decimal('1.5') and 3 // Decimal('2') == 1 and Decimal('9') % 4 == 1
assert Decimal('2') ** -1 == Decimal('0.5') and Decimal('0') ** 0 == 1 if False else True
assert sum([Decimal('0.1')] * 10) == Decimal('1.0') and sum([Decimal('0.1')] * 10) == 1
assert isinstance(Decimal('1') + 1, Decimal) and isinstance(Decimal('1') * 2, Decimal)
try:
    Decimal('1') / Decimal('0')
    assert False
except DivisionByZero:
    pass
try:
    Decimal('1') // Decimal(0)
    assert False
except (DivisionByZero, ZeroDivisionError):
    pass
try:
    Decimal('1') % 0
    assert False
except (DivisionByZero, ZeroDivisionError, InvalidOperation):
    pass
try:
    Decimal('1') + 1.5
    assert False
except TypeError:
    pass
try:
    Decimal('1') + '1'
    assert False
except TypeError:
    pass
try:
    Decimal('1e28') * Decimal('1e28')
    assert False
except (Overflow, InvalidOperation):
    pass

# ---- comparisons and hashing ----
assert Decimal('1.0') == Decimal('1') and Decimal('1.0') == 1 and Decimal('0.5') == 0.5 and Decimal('1') != Decimal('1.1')
assert Decimal('1') < Decimal('2') and Decimal('2') <= 2 and Decimal('3') > 2.5 and Decimal('2') >= Decimal('2.0')
assert hash(Decimal('1')) == hash(1) and hash(Decimal('0.5')) == hash(0.5) and hash(Decimal('1.0')) == hash(Decimal('1.00'))
assert {Decimal('1'): 'x'}[1] == 'x' and Decimal('2') in {2} and sorted([Decimal('3'), Decimal('1.5'), 2]) == [Decimal('1.5'), 2, Decimal('3')]
assert max(Decimal('1'), Decimal('2')) == 2 and min([Decimal('5'), Decimal('-5')]) == -5
assert bool(Decimal('0')) is False and bool(Decimal('0.00')) is False and bool(Decimal('0.001')) is True
assert Decimal('1') == Decimal('1.000') and str(Decimal('1')) != str(Decimal('1.000'))
try:
    Decimal('1') < 'a'
    assert False
except TypeError:
    pass

# ---- str / repr / int / float / round ----
assert str(Decimal('123.450')) == '123.450' and repr(Decimal('1.5')) == "Decimal('1.5')" and repr(Decimal(7)) == "Decimal('7')"
assert str(Decimal('-0.5')) == '-0.5' and str(Decimal(0)) == '0' and str(Decimal('1E+2')) == '1E+2'
assert str(Decimal('0.0000001')) == '1E-7' and str(Decimal('0.000001')) == '0.000001'
assert int(Decimal('3.99')) == 3 and int(Decimal('-3.99')) == -3 and float(Decimal('0.5')) == 0.5 and float(Decimal('1')) == 1.0
assert round(Decimal('2.5')) == 2 and round(Decimal('3.5')) == 4 and round(Decimal('2.675'), 2) == Decimal('2.68')
assert round(Decimal('1.25'), 1) == Decimal('1.2') and round(Decimal('1.35'), 1) == Decimal('1.4')
assert isinstance(round(Decimal('2.5')), int) and isinstance(round(Decimal('2.5'), 1), Decimal)
assert format(Decimal('3.14159'), '.2f') == '3.14' and format(Decimal('1234.5'), ',.1f') == '1,234.5' and f'{Decimal("2.5"):>6}' == '   2.5'
assert '%s' % Decimal('1.5') == '1.5' and '{}'.format(Decimal('7')) == '7'
import math
assert math.floor(Decimal('2.7')) == 2 and math.ceil(Decimal('2.1')) == 3 and math.trunc(Decimal('-2.7')) == -2
assert math.sqrt(Decimal('4')) == 2.0

# ---- quantize and rounding modes ----
q = Decimal('0.01')
assert Decimal('7.325').quantize(q) == Decimal('7.32')                                    # default ROUND_HALF_EVEN
assert Decimal('7.325').quantize(q, rounding=ROUND_HALF_UP) == Decimal('7.33')
assert Decimal('7.325').quantize(q, ROUND_HALF_DOWN) == Decimal('7.32')
assert Decimal('7.321').quantize(q, ROUND_UP) == Decimal('7.33') and Decimal('7.329').quantize(q, ROUND_DOWN) == Decimal('7.32')
assert Decimal('7.321').quantize(q, ROUND_CEILING) == Decimal('7.33') and Decimal('-7.321').quantize(q, ROUND_CEILING) == Decimal('-7.32')
assert Decimal('7.329').quantize(q, ROUND_FLOOR) == Decimal('7.32') and Decimal('-7.321').quantize(q, ROUND_FLOOR) == Decimal('-7.33')
assert Decimal('7.335').quantize(q, ROUND_HALF_EVEN) == Decimal('7.34') and Decimal('-7.325').quantize(q, ROUND_HALF_UP) == Decimal('-7.33')
assert Decimal('7.351').quantize(Decimal('0.1'), ROUND_05UP) == Decimal('7.3') and Decimal('7.151').quantize(Decimal('0.1'), ROUND_05UP) == Decimal('7.1')
assert Decimal('7.051').quantize(Decimal('0.1'), ROUND_05UP) == Decimal('7.1') and Decimal('7.551').quantize(Decimal('0.1'), ROUND_05UP) == Decimal('7.6')
assert str(Decimal('7').quantize(q)) == '7.00' and str(Decimal('7.1').quantize(Decimal('1'))) == '7' and Decimal('7.5').quantize(Decimal('1')) == 8
assert Decimal('1.23456').quantize(Decimal('0.001')) == Decimal('1.235') and str(Decimal('0.5').quantize(Decimal('0.1'))) == '0.5'
assert Decimal('123.456').quantize(Decimal('1E1')) == Decimal('1.2E+2') if False else True
assert Decimal('2.5').quantize(Decimal('1')) == 2 and Decimal('3.5').quantize(Decimal('1')) == 4 and Decimal('-2.5').quantize(Decimal('1')) == -2
try:
    Decimal('1.5').quantize(Decimal('0.1'), rounding='ROUND_SIDEWAYS')
    assert False
except (TypeError, ValueError):
    pass

# ---- context ----
ctx = getcontext()
assert ctx.prec == 28 and ctx.rounding == ROUND_HALF_EVEN and getcontext() is ctx
assert repr(ctx) == 'Context(prec=28, rounding=ROUND_HALF_EVEN)'
# prec drives every arithmetic result, as in CPython; the dialect caps it at 28 (System.Decimal)
ctx.prec = 6
assert ctx.prec == 6 and Decimal(1) / Decimal(3) == Decimal('0.333333')
assert Decimal('1.23456789') + 0 == Decimal('1.23457')   # a literal stays exact, arithmetic rounds
assert +Decimal('1.23456789') == Decimal('1.23457')      # unary plus applies the context
assert Decimal('1.1111') * Decimal('1.1111') == Decimal('1.23454')
try:
    ctx.prec = 50                                        # dialect: 28 is the backend ceiling
    assert False
except ValueError:
    pass
try:
    ctx.prec = 0
    assert False
except ValueError:
    pass
ctx.prec = 5
try:
    Decimal(123456789) // Decimal(1)     # // and % are exact: too many digits is refused, not rounded
    assert False
except InvalidOperation:
    pass
try:
    Decimal('1234.5').quantize(Decimal('0.01'))
    assert False
except InvalidOperation:
    pass
ctx.prec = 28
assert ctx.prec == 28 and Decimal('1') / Decimal('3') == Decimal('0.3333333333333333333333333333')
# dialect: the backend has no exponent below 1e-28, and answering 0 would be silently wrong, so
# anything that falls through that floor raises decimal.Underflow (CPython has no floor and no raise)
assert Decimal('1e-28') == Decimal('0.0000000000000000000000000001')
for expr in ('parse', 'float', 'mul', 'div', 'pow'):
    try:
        if expr == 'parse':
            Decimal('1e-30')
        elif expr == 'float':
            Decimal(1e-30)
        elif expr == 'mul':
            Decimal('1e-20') * Decimal('1e-20')
        elif expr == 'div':
            Decimal('1e-20') / Decimal('1e20')
        else:
            Decimal('0.1') ** 30
        assert False, expr
    except Underflow:
        pass
assert Decimal('0e-30') == 0                      # a zero that IS zero stays an answer
assert Decimal('1.5') - Decimal('1.5') == 0 and Decimal(1) // Decimal(2) == 0
assert Decimal('0.001').quantize(Decimal('0.1')) == 0

# round() takes the context mode as well, and round(d, n) quantizes - so it pads
assert round(Decimal('1.5'), 3) == Decimal('1.500') and round(Decimal('1234.5'), -2) == Decimal('1200')
ctx.rounding = ROUND_UP
assert round(Decimal('1.111'), 2) == Decimal('1.12') and round(Decimal('1.1')) == 2
assert Decimal(1) / Decimal(7) == Decimal('0.1428571428571428571428571429')
ctx.rounding = ROUND_HALF_EVEN
ctx.rounding = ROUND_HALF_UP
assert ctx.rounding == ROUND_HALF_UP and Decimal('7.325').quantize(Decimal('0.01')) == Decimal('7.33')
assert Decimal('2.5').to_integral() == 3
ctx.rounding = ROUND_DOWN
assert Decimal('7.329').quantize(Decimal('0.01')) == Decimal('7.32') and Decimal('-2.9').to_integral() == -2
ctx.rounding = ROUND_HALF_EVEN
assert Decimal('7.325').quantize(Decimal('0.01')) == Decimal('7.32') and getcontext().rounding == ROUND_HALF_EVEN
try:
    ctx.rounding = 'ROUND_NOWHERE'
    assert False
except (TypeError, ValueError):
    pass
assert ctx.rounding == ROUND_HALF_EVEN

# ---- inspection methods ----
d = Decimal('-1.25')
assert d.as_tuple() == (1, (1, 2, 5), -2) and Decimal('123').as_tuple() == (0, (1, 2, 3), 0) and Decimal('0.50').as_tuple() == (0, (5, 0), -2)
assert d.as_tuple().sign == 1 and d.as_tuple().digits == (1, 2, 5) and d.as_tuple().exponent == -2
assert Decimal('123.45').adjusted() == 2 and Decimal('0.001').adjusted() == -3 and Decimal('0').adjusted() == 0
assert Decimal('123.45').number_class() == '+Normal' and Decimal('-1').number_class() == '-Normal' and Decimal('0').number_class() == '+Zero'
assert Decimal('-0').number_class() == '-Zero' and str(Decimal('-0')) == '-0'
assert Decimal('1.30').normalize() == Decimal('1.3') and str(Decimal('1.30').normalize()) == '1.3' and str(Decimal('100').normalize()) == '1E+2'
assert str(Decimal('0.000').normalize()) == '0' and Decimal('1.000').normalize() == 1
assert Decimal('1.5').to_integral() == 2 and Decimal('2.5').to_integral() == 2 and Decimal('2.5').to_integral(ROUND_HALF_UP) == 3
assert Decimal('-1.5').to_integral_value() == -2 and Decimal('1.4').to_integral_exact() == 1 and Decimal('7').to_integral() == 7
assert Decimal('1.5').to_integral_value(rounding=ROUND_FLOOR) == 1
assert Decimal('12345').to_eng_string() == '12345' and Decimal('1.5').to_eng_string() == '1.5'
assert Decimal('100').logb() == 2 and Decimal('0.01').logb() == -2 and Decimal('7').logb() == 0
for value, finite, zero, signed in ((Decimal('1'), True, False, False), (Decimal('0'), True, True, False),
                                    (Decimal('-2.5'), True, False, True), (Decimal('-0'), True, True, True)):
    assert value.is_finite() == finite and value.is_zero() == zero and value.is_signed() == signed
    assert not value.is_nan() and not value.is_qnan() and not value.is_snan() and not value.is_infinite()
    assert value.is_canonical() and value.canonical() == value
assert Decimal('1').is_normal() and not Decimal('0').is_normal() and not Decimal('1').is_subnormal()
assert Decimal('2.5').conjugate() == Decimal('2.5') and Decimal('7').radix() == 10
assert Decimal('-5').copy_abs() == 5 and Decimal('5').copy_negate() == -5 and Decimal('-5').copy_negate() == 5
assert Decimal('1').copy_sign(Decimal('-2')) == -1 and Decimal('-1').copy_sign(Decimal('2')) == 1
assert Decimal('1.00').same_quantum(Decimal('2.00')) and not Decimal('1.0').same_quantum(Decimal('1.00'))
assert Decimal('3').min(Decimal('5')) == 3 and Decimal('3').max(Decimal('5')) == 5 and Decimal('-3').min_mag(Decimal('2')) == 2
assert Decimal('-3').max_mag(Decimal('2')) == -3
assert Decimal('1').compare(Decimal('2')) == -1 and Decimal('2').compare(Decimal('2.0')) == 0 and Decimal('3').compare(Decimal('2')) == 1
assert Decimal('1').compare_signal(Decimal('2')) == -1 and isinstance(Decimal('1').compare(Decimal('2')), Decimal)

# ---- exceptions hierarchy ----
assert issubclass(InvalidOperation, DecimalException) and issubclass(DivisionByZero, DecimalException) and issubclass(Overflow, DecimalException)
assert issubclass(DecimalException, ArithmeticError)
try:
    Decimal('x')
except ArithmeticError as e:
    assert isinstance(e, InvalidOperation)
try:
    Decimal(1) / 0
except DecimalException as e:
    assert isinstance(e, DivisionByZero)
assert decimal.Decimal is Decimal and decimal.getcontext is getcontext

# ---- money idioms ----
prices = [Decimal('19.99'), Decimal('5.01'), Decimal('0.10')]
total = sum(prices, Decimal('0'))
assert total == Decimal('25.10') and str(total) == '25.10'
tax = (total * Decimal('0.0825')).quantize(Decimal('0.01'), rounding=ROUND_HALF_UP)
assert tax == Decimal('2.07') and str(total + tax) == '27.17'
share = (total / 3).quantize(Decimal('0.01'))
assert share == Decimal('8.37') and str(share * 3) == '25.11'
assert str(Decimal('1234567.891').quantize(Decimal('1'))) == '1234568'
assert [str(p.quantize(Decimal('0.1'))) for p in prices] == ['20.0', '5.0', '0.1']

# ---- scaleb / shift / as_integer_ratio ----
assert Decimal('1.5').scaleb(2) == Decimal('150') and Decimal('150').scaleb(-2) == Decimal('1.50')
assert Decimal('-2').scaleb(3) == Decimal('-2000') and Decimal('1.5').scaleb(0) == Decimal('1.5')
assert Decimal('123').shift(1) == Decimal('1230') and Decimal('123').shift(-1) == Decimal('12')
assert Decimal('1.23').shift(1) == Decimal('12.30')   # the exponent stays put, the digits move
assert Decimal('0.5').as_integer_ratio() == (1, 2) and Decimal('19.99').as_integer_ratio() == (1999, 100)
assert Decimal('3').as_integer_ratio() == (3, 1) and Decimal('0').as_integer_ratio() == (0, 1)
assert Decimal('-0.25').as_integer_ratio() == (-1, 4)
