from decimal import Decimal, getcontext, localcontext
import decimal
out = []
def t(name, f):
    try:
        r = f()
        out.append(name + '=' + repr(r)[:80])
    except Exception as e:
        out.append(name + '!' + type(e).__name__ + ':' + str(e)[:80])
t('d_10_28', lambda: Decimal(10**28))
t('d_10_29', lambda: Decimal(10**29))
t('d_10_100', lambda: len(str(Decimal(10**100))))
t('d_10_1000', lambda: len(str(Decimal(10**1000))))
t('d_10_4300', lambda: len(str(Decimal(10**4300))))
t('d_2_64', lambda: Decimal(2**64))
t('d_neg_big', lambda: len(str(Decimal(-10**200))))
t('d_str_5000', lambda: len(str(Decimal('1' * 5000))))
t('d_add_big', lambda: Decimal(10**100) + 1)
t('d_from_float_big', lambda: len(str(Decimal(1e300))))
t('d_cmp_int_big', lambda: Decimal(1) < 10**100)
t('d_eq_int_big', lambda: Decimal('1e100') == 10**100)
t('d_hash_int_big', lambda: hash(Decimal('1e100')) == hash(10**100))
t('d_mul_int_big', lambda: Decimal(2) * 10**50)
t('d_int_back', lambda: int(Decimal('1e5000')) == 10**5000)
t('d_quant_big', lambda: Decimal(10**40).quantize(Decimal(1)))
t('d_ctx_create', lambda: localcontext(prec=5000).__enter__() and len(str(Decimal(10**4300) + 1)))
t('d_tuple', lambda: Decimal((0, (1,) * 100, 0)))
t('d_tuple_big', lambda: len(str(Decimal((0, (1,) * 5000, 0)))))
t('d_sci_big', lambda: Decimal('1e999999999'))
t('d_sci_over', lambda: Decimal('1e1000000000'))
t('d_sci_over2', lambda: Decimal('1e99999999999999999999'))
t('d_sci_under', lambda: Decimal('1e-99999999999999999999'))
t('d_plus_over', lambda: +Decimal('1e1000000000'))
print(chr(10).join(out))
