using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Hosting;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Values;
using Outbridge.PyLite.Syntax;

namespace Outbridge.PyLite.Runtime
{
    // The single value-allocation point: charges the budget BEFORE allocation and enforces object
    // caps. One instance per EvalContext; only the immutable caches are static.
    public sealed class ValueFactory
    {
        private readonly Budget _budget;
        private readonly ResourceLimits _limits;
        private long _identitySeq;
        internal EvalContext OwnerCtx;   // set by EvalContext ctor; used to build OrderedTable-backed values

        private static readonly StrValue[] AsciiCache = BuildAscii();

        public ValueFactory(Budget budget, ResourceLimits limits)
        {
            _budget = budget;
            _limits = limits;
        }

        private static StrValue[] BuildAscii()
        {
            var a = new StrValue[128];
            for (int i = 0; i < 128; i++)
                a[i] = new StrValue(((char)i).ToString(), true);   // shared => no per-seed hash caching
            return a;
        }

        public NoneValue None { get { return NoneValue.Instance; } }
        public BoolValue Bool(bool b) { return b ? BoolValue.True : BoolValue.False; }

        public IntValue Int(long v)
        {
            if (v >= IntValue.CacheLow && v <= IntValue.CacheHigh)
                return IntValue.Cached(v);
            // P1 fast path: same MaxIntBits gate + allocation charge as the BigInteger route, no BigInteger.
            long bits = NumericOps.BitLengthLong(v);
            if (bits > _limits.MaxIntBits)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", _limits.MaxIntBits, bits);
            _budget.ChargeAllocation(32 + (bits + 7) / 8);
            return new IntValue(v);
        }

        private static readonly BigInteger LongMinBig = long.MinValue;
        private static readonly BigInteger LongMaxBig = long.MaxValue;

        public IntValue Int(BigInteger v)
        {
            if (v >= LongMinBig && v <= LongMaxBig)
                return Int((long)v);
            long bits = NumericOps.BitLength(v);
            if (bits > _limits.MaxIntBits)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", _limits.MaxIntBits, bits);
            _budget.ChargeAllocation(32 + (bits + 7) / 8);
            var iv = new IntValue(v);
            iv.PrefillBits(bits);   // already computed for the charge — seed the per-value cache
            return iv;
        }

        public FloatValue Float(double d) { _budget.ChargeAllocation(24); return new FloatValue(d); }

        public StrValue Str(string s)
        {
            if (s.Length == 0)
                return StrValue.Empty;
            if (s.Length > _limits.MaxStrChars)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", _limits.MaxStrChars, s.Length);
            _budget.ChargeAllocation(24 + 2L * s.Length);
            return new StrValue(s);
        }

        public StrValue StrFromChar(char c)
        {
            if (c < 128)
                return AsciiCache[c];
            _budget.ChargeAllocation(26);
            return new StrValue(c.ToString());
        }

        public static readonly BytesValue EmptyBytes = new BytesValue(Array.Empty<byte>());

        public BytesValue Bytes(byte[] data)
        {
            if (data.Length == 0)
                return EmptyBytes;
            if (data.Length > _limits.MaxStrChars)
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", _limits.MaxStrChars, data.Length);
            _budget.ChargeAllocation(24 + data.Length);
            return new BytesValue(data);
        }

        public IteratorValue Iterator(IScriptIterator it, string typeName)
        {
            _budget.ChargeAllocation(48);
            return new IteratorValue(it, typeName, NextIdentitySeq());
        }

        public IteratorValue Iterator(IScriptIterator it, string typeName,
            System.Collections.Generic.IDictionary<string, SlotDescriptor> slots)
        {
            _budget.ChargeAllocation(48);
            return new IteratorValue(it, typeName, NextIdentitySeq(), slots);
        }

        public SliceValue Slice(ScriptValue start, ScriptValue stop, ScriptValue step)
        {
            _budget.ChargeAllocation(48);
            return new SliceValue(start, stop, step);
        }

        public static readonly TupleValue EmptyTuple = new TupleValue(Array.Empty<ScriptValue>());

        public ListValue List(int capacityHint)
        {
            int cap = capacityHint < 0 ? 0 : capacityHint;
            _budget.ChargeAllocation(56 + 8L * cap);
            return new ListValue(new List<ScriptValue>(cap));
        }

        public ListValue ListFrom(List<ScriptValue> adopt)
        {
            _budget.ChargeAllocation(56 + 8L * adopt.Capacity);
            return new ListValue(adopt);
        }

        public TupleValue Tuple(ScriptValue[] adopt)
        {
            if (adopt.Length == 0)
                return EmptyTuple;
            _budget.ChargeAllocation(40 + 8L * adopt.Length);
            return new TupleValue(adopt);
        }

        public DictValue Dict(int capacityHint)
        {
            return new DictValue(new OrderedTable(OwnerCtx, capacityHint));
        }

        // collections dict subtypes; the OrderedTable charges its own storage.
        internal OrderedDictValue OrderedDict(int capacityHint)
        {
            _budget.ChargeAllocation(16);
            return new OrderedDictValue(new OrderedTable(OwnerCtx, capacityHint));
        }

        internal DefaultDictValue DefaultDict(ScriptValue defaultFactory, int capacityHint)
        {
            _budget.ChargeAllocation(24);
            return new DefaultDictValue(new OrderedTable(OwnerCtx, capacityHint), defaultFactory);
        }

        internal CounterValue Counter(int capacityHint)
        {
            _budget.ChargeAllocation(16);
            return new CounterValue(new OrderedTable(OwnerCtx, capacityHint));
        }

        public SetValue Set(int capacityHint)
        {
            return new SetValue(new OrderedTable(OwnerCtx, capacityHint));
        }

        internal FrozenSetValue FrozenSet(OrderedTable adopt)
        {
            _budget.ChargeAllocation(32);
            return new FrozenSetValue(adopt);
        }

        public RangeValue Range(BigInteger start, BigInteger stop, BigInteger step)
        {
            _budget.ChargeAllocation(96);
            return new RangeValue(start, stop, step);
        }

        public FunctionValue Function(string name, FunctionInfo info, ScriptValue[] defaults,
            KeyValuePair<string, ScriptValue>[] kwDefaults, Cell[] closure, object globalsToken,
            Outbridge.PyLite.Syntax.ResolvedProgram program,
            System.Collections.Generic.IReadOnlyList<Outbridge.PyLite.Syntax.Ast.StmtNode> body = null,
            Outbridge.PyLite.Syntax.Ast.ExprNode lambdaBody = null, KeyValuePair<string, string>[] annotations = null)
        {
            int slots = (defaults != null ? defaults.Length : 0)
                + (kwDefaults != null ? kwDefaults.Length : 0)
                + (closure != null ? closure.Length : 0);
            _budget.ChargeAllocation(64 + 8L * slots);
            return new FunctionValue(name, info, defaults, kwDefaults, closure, globalsToken, program, body, lambdaBody, NextIdentitySeq(), annotations);
        }

        public BoundMethodValue BoundMethod(ScriptValue self, SlotDescriptor d)
        {
            _budget.ChargeAllocation(48);
            return new BoundMethodValue(self, d);
        }

        public BuiltinFunctionValue BuiltinFunction(string name, BuiltinDelegate fn)
        {
            _budget.ChargeAllocation(48);
            return new BuiltinFunctionValue(name, fn, NextIdentitySeq());
        }

        public ModuleValue Module(string name, IReadOnlyDictionary<string, ScriptValue> members)
        {
            _budget.ChargeAllocation(48);
            return new ModuleValue(name, members, NextIdentitySeq());
        }

        public TypeValue Type(string name, BuiltinDelegate constructor, ScriptTypeInfo describes,
            IReadOnlyDictionary<string, ScriptValue> staticSlots, Outbridge.PyLite.Runtime.Errors.PyExceptionType excType = null)
        {
            _budget.ChargeAllocation(48);
            return new TypeValue(name, constructor, describes, staticSlots, excType, NextIdentitySeq());
        }

        // --------- PreCharge protocol: cap-check BEFORE any allocation ----------

        // str result of resultChars: cap MaxStrChars, then charge the byte estimate (24 + 2*len).
        public void EnsureStrLen(long resultChars)
        {
            if (resultChars < 0 || resultChars > _limits.MaxStrChars)
            {
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxStrChars", _limits.MaxStrChars,
                    resultChars < 0 ? long.MaxValue : resultChars);
            }
            _budget.PreCharge(24 + 2L * resultChars);
        }

        // int result of resultBits: cap MaxIntBits, then charge 40 + 8*ceil(bits/64).
        public void EnsureIntBits(long resultBits)
        {
            if (resultBits < 0 || resultBits > _limits.MaxIntBits)
            {
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxIntBits", _limits.MaxIntBits,
                    resultBits < 0 ? long.MaxValue : resultBits);
            }
            _budget.PreCharge(40 + 8L * ((resultBits + 63) / 64));
        }

        // collection result of resultItems: cap only (elements are charged as they are added).
        public void EnsureCollectionSize(long resultItems)
        {
            if (resultItems < 0 || resultItems > _limits.MaxCollectionItems)
            {
                throw _budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", _limits.MaxCollectionItems,
                    resultItems < 0 ? long.MaxValue : resultItems);
            }
        }

        // Build a StrValue whose bytes were already charged via an explicit Ensure*/PreCharge — no double charge.
        public StrValue StrPrecharged(string s)
        {
            if (s.Length == 0)
            {
                return StrValue.Empty;
            }
            System.Diagnostics.Debug.Assert(s.Length <= _limits.MaxStrChars, "StrPrecharged over the str cap");
            return new StrValue(s);
        }

        // ---------- charged working buffers (A8-#7): cumulative, no release ----------

        public System.Text.StringBuilder RentBuilder(int capacityHint)
        {
            if (capacityHint < 0)
            {
                throw new InvalidOperationException("negative builder capacity");
            }
            _budget.ChargeAllocation(24 + 2L * capacityHint);
            return new System.Text.StringBuilder(capacityHint);
        }

        // Call BEFORE Append; charges the growth of the builder's backing store.
        public void ChargeBuilderGrow(System.Text.StringBuilder sb, int addedChars)
        {
            _budget.ChargeAllocation(2L * addedChars);
        }

        // ---------- incremental container growth (called by containers on Add/Insert) ----------
        public void ChargeListGrow(int newCapacity, int oldCapacity) { _budget.ChargeAllocation(8L * (newCapacity - oldCapacity)); }
        public void ChargeDictEntry() { _budget.ChargeAllocation(48); }
        public void ChargeSetEntry() { _budget.ChargeAllocation(32); }

        public ExceptionValue Exception(PyExceptionType t, ScriptValue[] args)
        {
            _budget.ChargeAllocation(64 + 8L * (args != null ? args.Length : 0));
            return new ExceptionValue(t, args);
        }

        public void PreCharge(long estimatedBytes) { _budget.PreCharge(estimatedBytes); }
        internal void ChargeBytes(long bytes) { _budget.ChargeAllocation(bytes); }
        internal long NextIdentitySeq() { return ++_identitySeq; }

        // id(): a number handed out on first ask and remembered, so id(a) == id(b) exactly when `a is b`
        // (which is ReferenceEquals). A counter rather than an address keeps runs reproducible, and the
        // weak table lets a value the script dropped be collected anyway.
        private System.Runtime.CompilerServices.ConditionalWeakTable<ScriptValue, object> _ids;

        internal long IdOf(ScriptValue v)
        {
            if (_ids == null)
                _ids = new System.Runtime.CompilerServices.ConditionalWeakTable<ScriptValue, object>();
            object known;
            if (_ids.TryGetValue(v, out known))
                return (long)known;
            _budget.ChargeAllocation(48);
            long id = NextIdentitySeq();
            _ids.Add(v, id);
            return id;
        }
        internal Budget Budget { get { return _budget; } }
        internal ResourceLimits Limits { get { return _limits; } }
    }
}
