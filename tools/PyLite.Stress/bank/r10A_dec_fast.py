from decimal import Decimal as D, getcontext
import decimal
vals = ["0", "-0", "1", "-1", "7", "-7", "0.5", "-0.50", "1.00", "123.456", "-123.456", "1E+2", "-1E+3", "1E-5",
        "0.000", "-0.000", "18446744073709551615", "18446744073709551616", "1844674407370955161.5",
        "9999999999999999999", "0.0000000000000000000000000001", "4294967295", "4294967296", "3.1", "2.50",
        "79228162514264337593543950335", "1.0000000000000000001", "10", "100.0", "0E+5", "5E-28", "1E-28"]
out = []
def tryop(f):
    try:
        r = f()
        if isinstance(r, tuple):
            return "(" + ", ".join(repr(x) for x in r) + ")"
        return repr(r)
    except Exception as e:
        return type(e).__name__
for prec in (28, 5, 1, 20, 19):
    getcontext().prec = prec
    for a in vals:
        for b in vals:
            x = D(a); y = D(b)
            out.append("p%d %s // %s = %s" % (prec, a, b, tryop(lambda: x // y)))
            out.append("p%d %s %% %s = %s" % (prec, a, b, tryop(lambda: x % y)))
            out.append("p%d divmod %s %s = %s" % (prec, a, b, tryop(lambda: divmod(x, y))))
        for n in (1, 2, 3, 5, 13, 19, 20, 27, 28, 29, 63, 64, 65, -1, -2):
            out.append("p%d %s ** %d = %s" % (prec, a, n, tryop(lambda: D(a) ** n)))
            out.append("p%d %s ** D%d.0 = %s" % (prec, a, n, tryop(lambda: D(a) ** D(str(n) + ".0"))))
    for a in vals:
        for i in (0, 1, -1, 7, 2**63, 2**64, True):
            x = D(a)
            out.append("p%d %s // int %s = %s" % (prec, a, i, tryop(lambda: x // i)))
            out.append("p%d int %s %% %s = %s" % (prec, i, a, tryop(lambda: i % x)))
getcontext().prec = 28
import hashlib
s = "\n".join(out)
print(len(out), hashlib.sha256(s.encode()).hexdigest() if hasattr(hashlib, "sha256") else "")
for line in out:
    print(line)
