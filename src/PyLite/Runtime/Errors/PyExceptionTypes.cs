using System;
using System.Collections.Generic;

namespace Outbridge.PyLite.Runtime.Errors
{
    // The single definition point of the 21-node exception tree.
    // SystemExit/KeyboardInterrupt/MemoryError are intentionally absent — EngineAbort plays their role;
    // GetByName returns null for them. RecursionError exists for the DATA-depth cap (MaxDataDepth on
    // nested containers and records), which a script may catch as it does in CPython; the CALL-depth
    // cap stays an EngineAbort, because a script must not be able to swallow that one.
    public static class PyExceptionTypes
    {
        public static readonly PyExceptionType BaseException_;
        public static readonly PyExceptionType Exception_;
        public static readonly PyExceptionType ArithmeticError;
        public static readonly PyExceptionType ZeroDivisionError;
        public static readonly PyExceptionType OverflowError;
        public static readonly PyExceptionType FloatingPointError;
        public static readonly PyExceptionType AssertionError;
        public static readonly PyExceptionType AttributeError;
        public static readonly PyExceptionType ImportError;
        public static readonly PyExceptionType OSError;
        public static readonly PyExceptionType LookupError;
        public static readonly PyExceptionType IndexError;
        public static readonly PyExceptionType KeyError;
        public static readonly PyExceptionType NameError;
        public static readonly PyExceptionType UnboundLocalError;
        public static readonly PyExceptionType RuntimeError;
        public static readonly PyExceptionType RecursionError;
        public static readonly PyExceptionType NotImplementedError;
        public static readonly PyExceptionType StopIteration;
        public static readonly PyExceptionType TypeError;
        public static readonly PyExceptionType ValueError;
        public static readonly PyExceptionType UnicodeError;
        public static readonly PyExceptionType UnicodeDecodeError;
        public static readonly PyExceptionType UnicodeEncodeError;
        public static readonly PyExceptionType StatisticsError;
        public static readonly PyExceptionType JSONDecodeError;

        // re.error — a direct subclass of Exception. Deliberately NOT in AllArr/ByName: it is exposed only as
        // `re.error` (the re module), never as a global builtin.
        public static readonly PyExceptionType ReError;

        // decimal exception tree. Exposed only via the decimal module (like re.error) — kept out
        // of AllArr/ByName. DecimalException <- ArithmeticError; the signals <- DecimalException. Underflow is
        // ours to raise: CPython never traps it, but on this backend it means the value fell through the floor.
        public static readonly PyExceptionType DecimalException;
        public static readonly PyExceptionType DecimalInvalidOperation;
        public static readonly PyExceptionType DecimalDivisionByZero;
        public static readonly PyExceptionType DecimalOverflow;
        public static readonly PyExceptionType DecimalUnderflow;
        public static readonly PyExceptionType ZoneInfoNotFoundError;
        public static readonly PyExceptionType IllegalMonthError;
        public static readonly PyExceptionType IllegalWeekdayError;

        // copy.Error — a direct subclass of Exception, exposed only as `copy.Error`.
        public static readonly PyExceptionType CopyError;

        // SyntaxError exists for scripts to NAME - `except (ValueError, SyntaxError)` is ordinary portable
        // code - and as ParseError's parent, exactly as in CPython. The engine's own compile errors are
        // still a host-facing kind, never a script-catchable value: there is no eval/exec to produce one.
        public static readonly PyExceptionType SyntaxError;

        // xml.etree.ElementTree.ParseError(SyntaxError), exposed only via the xml module.
        public static readonly PyExceptionType XmlParseError;

        // binascii.Error — subclass of ValueError (CPython), exposed only via the binascii module.
        public static readonly PyExceptionType BinasciiError;

        // struct.error — direct subclass of Exception, exposed only via the struct module.
        public static readonly PyExceptionType StructError;

        // csv.Error — direct subclass of Exception, exposed only via the csv module.
        public static readonly PyExceptionType CsvError;

        // zlib.error / gzip.BadGzipFile (an OSError, as in CPython).
        public static readonly PyExceptionType ZlibError;
        public static readonly PyExceptionType BadGzipFile;

        private static readonly PyExceptionType[] AllArr;
        private static readonly Dictionary<string, PyExceptionType> ByName;

        static PyExceptionTypes()
        {
            BaseException_ = new PyExceptionType("BaseException", null);
            Exception_ = new PyExceptionType("Exception", BaseException_);
            ArithmeticError = new PyExceptionType("ArithmeticError", Exception_);
            ZeroDivisionError = new PyExceptionType("ZeroDivisionError", ArithmeticError);
            OverflowError = new PyExceptionType("OverflowError", ArithmeticError);
            FloatingPointError = new PyExceptionType("FloatingPointError", ArithmeticError);
            AssertionError = new PyExceptionType("AssertionError", Exception_);
            AttributeError = new PyExceptionType("AttributeError", Exception_);
            ImportError = new PyExceptionType("ImportError", Exception_);
            OSError = new PyExceptionType("OSError", Exception_);
            LookupError = new PyExceptionType("LookupError", Exception_);
            IndexError = new PyExceptionType("IndexError", LookupError);
            KeyError = new PyExceptionType("KeyError", LookupError);
            NameError = new PyExceptionType("NameError", Exception_);
            UnboundLocalError = new PyExceptionType("UnboundLocalError", NameError);
            RuntimeError = new PyExceptionType("RuntimeError", Exception_);
            RecursionError = new PyExceptionType("RecursionError", RuntimeError);
            NotImplementedError = new PyExceptionType("NotImplementedError", RuntimeError);
            StopIteration = new PyExceptionType("StopIteration", Exception_);
            TypeError = new PyExceptionType("TypeError", Exception_);
            ValueError = new PyExceptionType("ValueError", Exception_);
            UnicodeError = new PyExceptionType("UnicodeError", ValueError);
            UnicodeDecodeError = new PyExceptionType("UnicodeDecodeError", UnicodeError);
            UnicodeEncodeError = new PyExceptionType("UnicodeEncodeError", UnicodeError);
            StatisticsError = new PyExceptionType("StatisticsError", ValueError);
            JSONDecodeError = new PyExceptionType("JSONDecodeError", ValueError);
            ReError = new PyExceptionType("error", Exception_);   // re.error
            DecimalException = new PyExceptionType("DecimalException", ArithmeticError);
            DecimalInvalidOperation = new PyExceptionType("InvalidOperation", DecimalException);
            DecimalDivisionByZero = new PyExceptionType("DivisionByZero", DecimalException);
            DecimalOverflow = new PyExceptionType("Overflow", DecimalException);
            DecimalUnderflow = new PyExceptionType("Underflow", DecimalException);
            CopyError = new PyExceptionType("Error", Exception_);   // copy.Error
            SyntaxError = new PyExceptionType("SyntaxError", Exception_);
            XmlParseError = new PyExceptionType("ParseError", SyntaxError);   // xml ParseError
            BinasciiError = new PyExceptionType("Error", ValueError);       // binascii.Error
            StructError = new PyExceptionType("error", Exception_);        // struct.error
            CsvError = new PyExceptionType("Error", Exception_);           // csv.Error
            ZlibError = new PyExceptionType("error", Exception_);          // zlib.error
            BadGzipFile = new PyExceptionType("BadGzipFile", OSError);      // gzip.BadGzipFile
            ZoneInfoNotFoundError = new PyExceptionType("ZoneInfoNotFoundError", KeyError);   // zoneinfo
            IllegalMonthError = new PyExceptionType("IllegalMonthError", ValueError);          // calendar
            IllegalWeekdayError = new PyExceptionType("IllegalWeekdayError", ValueError);      // calendar

            AllArr = new[]
            {
                BaseException_, Exception_, ArithmeticError, ZeroDivisionError, OverflowError,
                FloatingPointError, AssertionError, AttributeError, ImportError, OSError, LookupError,
                IndexError, KeyError, NameError, UnboundLocalError, RuntimeError, RecursionError,
                NotImplementedError, StopIteration, TypeError, ValueError, UnicodeError, UnicodeDecodeError,
                UnicodeEncodeError, StatisticsError, JSONDecodeError, SyntaxError
            };
            ByName = new Dictionary<string, PyExceptionType>(StringComparer.Ordinal);
            foreach (PyExceptionType t in AllArr)
                ByName[t.Name] = t;
        }

        public static PyExceptionType GetByName(string name)
        {
            PyExceptionType t;
            return ByName.TryGetValue(name, out t) ? t : null;
        }

        public static IReadOnlyList<PyExceptionType> All { get { return AllArr; } }
    }
}
