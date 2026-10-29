using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime.Evaluator;

namespace Outbridge.PyLite.Runtime.Values
{
    // The one slot-table builder for engine types. Linear registers a method that is at most one pass
    // over the receiver and charges the receiver's size before the body runs (Budget.ChargeLinear), so
    // a pass that forgets its own charge is still seen by the deadline and a new method cannot opt out
    // by omission. Method registers a constant-time one, or one whose cost is its argument's and is
    // charged there. The charge is a step per LinearChunkSize units, so a body that also steps per
    // element is not priced twice in any measurable way.
    internal sealed class Slots
    {
        private readonly Dictionary<string, SlotDescriptor> _table;
        private readonly Func<ScriptValue, int> _size;

        public Slots(Func<ScriptValue, int> size)
        {
            _size = size;
            _table = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
        }

        // Extends a base type's table (Counter over dict); the base entries keep their own policy.
        public Slots(Func<ScriptValue, int> size, IDictionary<string, SlotDescriptor> baseTable)
        {
            _size = size;
            _table = new Dictionary<string, SlotDescriptor>(baseTable, StringComparer.Ordinal);
        }

        public IDictionary<string, SlotDescriptor> Table { get { return _table; } }

        public Slots Linear(string name, BuiltinDelegate fn)
        {
            Func<ScriptValue, int> size = _size;
            _table[name] = SlotDescriptor.MakeMethod(name, (self, a, kw, c) =>
            {
                c.Budget.ChargeLinear(size(self));
                return fn(self, a, kw, c);
            });
            return this;
        }

        public Slots Method(string name, BuiltinDelegate fn)
        {
            _table[name] = SlotDescriptor.MakeMethod(name, fn);
            return this;
        }

        // The same for a method of min..max positional arguments and no keywords (CPython's METH_NOARGS,
        // METH_O and PyArg_ParseTuple methods): both are checked before the body, which can then neither
        // drop a keyword nor ignore an extra argument silently.
        public Slots Linear(string name, int min, int max, BuiltinDelegate fn)
        {
            Func<ScriptValue, int> size = _size;
            _table[name] = SlotDescriptor.MakeMethod(name, (self, a, kw, c) =>
            {
                if (kw.Count != 0 || a.Length < min || a.Length > max)
                    RefuseArguments(c, name, min, max, a, kw);
                c.Budget.ChargeLinear(size(self));
                return fn(self, a, kw, c);
            });
            return this;
        }

        public Slots Method(string name, int min, int max, BuiltinDelegate fn)
        {
            _table[name] = SlotDescriptor.MakeMethod(name, (self, a, kw, c) =>
            {
                if (kw.Count != 0 || a.Length < min || a.Length > max)
                    RefuseArguments(c, name, min, max, a, kw);
                return fn(self, a, kw, c);
            });
            return this;
        }

        public const int Many = int.MaxValue;   // max for a *args method (set.union)

        private static void RefuseArguments(EvalContext c, string name, int min, int max, ScriptValue[] a, KwArgs kw)
        {
            Modules.Support.Args.NoKw(c, kw, name);
            Modules.Support.Args.CheckPositional(c, a, name, min, max);
        }

        public Slots Property(string name, Func<ScriptValue, EvalContext, ScriptValue> getter)
        {
            _table[name] = SlotDescriptor.MakeProperty(name, getter);
            return this;
        }
    }
}
