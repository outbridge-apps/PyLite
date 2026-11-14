using System;
using System.Collections.Generic;
using System.Numerics;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Modules.Support;

namespace Outbridge.PyLite.Runtime.Values
{
    // collections.deque: our own ring buffer. Kind==Opaque; subscript/len/in/iter/==
    // route through the ScriptValue hooks. maxlen eviction is from the opposite end.
    internal sealed class DequeValue : ScriptValue
    {
        private static readonly ScriptTypeInfo DequeType = new ScriptTypeInfo("collections.deque", BuildSlots());

        private ScriptValue[] _buf;
        private int _head;
        private int _count;
        private readonly long _maxlen;   // -1 => unbounded
        private int _version;

        internal DequeValue(long maxlen)
        {
            _maxlen = maxlen;
            _buf = new ScriptValue[8];
        }

        public override ScriptTypeInfo TypeInfo { get { return DequeType; } }
        internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        internal int Version { get { return _version; } }
        internal int Count { get { return _count; } }
        internal long MaxLenRaw { get { return _maxlen; } }

        private int Phys(int i) { return (_head + i) % _buf.Length; }
        internal ScriptValue GetLogical(int i) { return _buf[Phys(i)]; }

        // CPython checks a deque's state after every comparison a walk makes: an __eq__ (or the __bool__ of
        // what it returned) that mutates the deque ends the walk with this error, not on a moved slot.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private void CheckUnchanged(int version, EvalContext ctx)
        {
            if (_version != version)
                ThrowMutated(ctx);   // out of line: a method that throws is not inlined
        }

        private static void ThrowMutated(EvalContext ctx)
        {
            throw Raise.RuntimeError(ctx, "deque mutated during iteration");
        }

        // ---- ScriptValue hooks ----

        protected internal override bool IsTruthyCore(EvalContext ctx) { return _count > 0; }
        protected internal override bool TryLengthCore(out long length) { length = _count; return true; }
        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new DequeIterator(this); }

        protected internal override IScriptIterator GetReverseIteratorCore(EvalContext ctx) { return new DequeReverseIterator(this); }

        protected internal override bool TryContainsCore(ScriptValue item, EvalContext ctx, int depth, out bool found)
        {
            int v = _version;
            for (int i = 0; i < _count; i++)
            {
                ctx.Budget.Step();
                if (PyOps.Equals(item, GetLogical(i), ctx, depth + 1))
                {
                    found = true;
                    return true;
                }
                CheckUnchanged(v, ctx);
            }
            found = false;
            return true;
        }

        protected internal override ScriptValue GetItemCore(ScriptValue index, EvalContext ctx)
        {
            int i = NormIndex(ctx, index);
            return GetLogical(i);
        }

        protected internal override bool TrySetItemCore(ScriptValue index, ScriptValue value, EvalContext ctx)
        {
            int i = NormIndex(ctx, index);
            _buf[Phys(i)] = value;   // not a mutation to an iterator: CPython's deque state counts size and order changes
            return true;
        }

        protected internal override bool TryDelItemCore(ScriptValue index, EvalContext ctx)
        {
            int i = NormIndex(ctx, index);
            RemoveAt(i, ctx);
            return true;
        }

        protected internal override bool EqualsLeafCore(ScriptValue other, EvalContext ctx, int depth)
        {
            DequeValue o = other as DequeValue;
            if (o == null)
                return ReferenceEquals(this, other);
            if (_count != o._count)
                return false;
            // As CPython compares two deques, by iterators: a mutation shows when the next pair is read, and
            // the end of an iteration is a read.
            int n = _count, v = _version, ov = o._version;
            for (int i = 0; i <= n; i++)
            {
                CheckUnchanged(v, ctx);
                o.CheckUnchanged(ov, ctx);
                if (i < n && !PyOps.Equals(GetLogical(i), o.GetLogical(i), ctx, depth + 1))
                    return false;
            }
            return true;
        }

        protected internal override bool TryCompareLeafCore(ScriptValue other, EvalContext ctx, int depth, out int cmp)
        {
            DequeValue o = other as DequeValue;
            if (o == null)
            {
                cmp = 0;
                return false;
            }
            int n = Math.Min(_count, o._count), v = _version, ov = o._version;
            for (int i = 0; i < n; i++)
            {
                CheckUnchanged(v, ctx);
                o.CheckUnchanged(ov, ctx);
                ScriptValue a = GetLogical(i), b = o.GetLogical(i);
                if (!PyOps.Equals(a, b, ctx, depth + 1))
                {
                    cmp = PyOps.RichCompare(PyCmpOp.Lt, a, b, ctx, depth + 1).IsTruthy(ctx) ? -1 : 1;
                    return true;
                }
            }
            CheckUnchanged(v, ctx);
            o.CheckUnchanged(ov, ctx);
            cmp = _count.CompareTo(o._count);
            return true;
        }

        protected internal override void ReprLeafCore(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            if (!ReprEngine.EnterLeaf(ctx, this))
            {
                sb.Append("[...]");   // CPython's deque marks itself as a list does
                return;
            }
            try
            {
                ReprBody(sb, ctx, depth);
            }
            finally
            {
                ReprEngine.ExitLeaf(ctx, this);
            }
        }

        private void ReprBody(BudgetStringBuilder sb, EvalContext ctx, int depth)
        {
            ListValue lst = ctx.Values.List(_count);
            for (int i = 0; i < _count; i++)
                lst.Add(GetLogical(i), ctx);
            sb.Append("deque(");
            sb.Append(lst.Repr(ctx, depth + 1));
            if (_maxlen >= 0)
            {
                sb.Append(", maxlen=");
                sb.Append(_maxlen.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            sb.Append(")");
        }

        // ---- indexing helpers ----

        private int NormIndex(EvalContext ctx, ScriptValue index)
        {
            if (index.Kind != ValueKind.Int && index.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "sequence index must be integer, not '" + index.PyTypeName + "'");
            int i;
            if (!SequenceOps.TryNormIndex(index, _count, out i))
                throw Raise.IndexError(ctx, "deque index out of range");
            return i;
        }

        // ---- mutation primitives ----

        private void EnsureCap(EvalContext ctx)
        {
            if (_count < _buf.Length)
                return;
            int newCap = _buf.Length < 8 ? 8 : _buf.Length * 2;
            ctx.Values.PreCharge(8L * newCap);
            var nb = new ScriptValue[newCap];
            for (int i = 0; i < _count; i++)
                nb[i] = GetLogical(i);
            _buf = nb;
            _head = 0;
        }

        private void AppendRaw(ScriptValue x, EvalContext ctx)
        {
            if (_count >= ctx.Limits.MaxCollectionItems)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems, _count + 1);
            EnsureCap(ctx);
            _buf[Phys(_count)] = x;
            _count++;
            _version++;
        }

        private void AppendLeftRaw(ScriptValue x, EvalContext ctx)
        {
            if (_count >= ctx.Limits.MaxCollectionItems)
                throw ctx.Budget.CreateAbort(EngineAbortKind.Memory, "MaxCollectionItems", ctx.Limits.MaxCollectionItems, _count + 1);
            EnsureCap(ctx);
            _head = (_head - 1 + _buf.Length) % _buf.Length;
            _buf[_head] = x;
            _count++;
            _version++;
        }

        private ScriptValue PopRaw()
        {
            int p = Phys(_count - 1);
            ScriptValue v = _buf[p];
            _buf[p] = null;
            _count--;
            _version++;
            return v;
        }

        private ScriptValue PopLeftRaw()
        {
            ScriptValue v = _buf[_head];
            _buf[_head] = null;
            _head = (_head + 1) % _buf.Length;
            _count--;
            _version++;
            return v;
        }

        internal void Append(ScriptValue x, EvalContext ctx)
        {
            if (_maxlen == 0)
                return;
            if (_maxlen > 0 && _count == _maxlen)
                PopLeftRaw();
            AppendRaw(x, ctx);
        }

        private void AppendLeft(ScriptValue x, EvalContext ctx)
        {
            if (_maxlen == 0)
                return;
            if (_maxlen > 0 && _count == _maxlen)
                PopRaw();
            AppendLeftRaw(x, ctx);
        }

        // Shallow clone preserving maxlen and element references (copy.copy / copy.deepcopy scaffolding).
        internal DequeValue CloneShallow(EvalContext ctx)
        {
            int cap = Math.Max(8, _count);
            ctx.Values.PreCharge(88 + 8L * cap);   // the object and its buffer: kept copies were charged a third of their size
            var d = new DequeValue(_maxlen) { _buf = new ScriptValue[cap] };
            for (int i = 0; i < _count; i++)
                d._buf[i] = GetLogical(i);
            d._count = _count;
            return d;
        }

        // Append without maxlen eviction (used by CloneShallow / deepcopy fill).
        internal void AppendRawPublic(ScriptValue x, EvalContext ctx) { AppendRaw(x, ctx); }

        // Shifts the shorter side, as CPython's deque does, and charges what it moves.
        private void RemoveAt(int i, EvalContext ctx)
        {
            if (i < _count / 2)
            {
                ctx.Budget.ChargeLinear(i);
                for (int j = i; j > 0; j--)
                    _buf[Phys(j)] = _buf[Phys(j - 1)];
                _buf[_head] = null;
                _head = (_head + 1) % _buf.Length;
            }
            else
            {
                ctx.Budget.ChargeLinear(_count - 1 - i);
                for (int j = i; j < _count - 1; j++)
                    _buf[Phys(j)] = _buf[Phys(j + 1)];
                _buf[Phys(_count - 1)] = null;
            }
            _count--;
            _version++;
        }

        // ---- method slots ----

        private static ScriptValue Append_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ((DequeValue)self).Append(Args.One(ctx, a, "append"), ctx);
            return ctx.Values.None;
        }

        private static ScriptValue AppendLeft_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            ((DequeValue)self).AppendLeft(Args.One(ctx, a, "appendleft"), ctx);
            return ctx.Values.None;
        }

        private static ScriptValue Pop_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            if (d._count == 0)
                throw Raise.IndexError(ctx, "pop from an empty deque");
            return d.PopRaw();
        }

        private static ScriptValue PopLeft_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            if (d._count == 0)
                throw Raise.IndexError(ctx, "pop from an empty deque");
            return d.PopLeftRaw();
        }

        private static ScriptValue Extend_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            IScriptIterator it = SourceIterator(d, Args.One(ctx, a, "extend"), ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                d.Append(v, ctx);
            return ctx.Values.None;
        }

        private static ScriptValue ExtendLeft_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            IScriptIterator it = SourceIterator(d, Args.One(ctx, a, "extendleft"), ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                d.AppendLeft(v, ctx);   // net effect: reversed
            return ctx.Values.None;
        }

        // d.extend(d) iterates a snapshot (CPython copies first), never the deque being mutated.
        private static IScriptIterator SourceIterator(DequeValue d, ScriptValue source, EvalContext ctx)
        {
            if (!ReferenceEquals(source, d))
                return PyOps.GetIterator(source, ctx);
            ListValue snapshot = ctx.Values.List(4);
            IScriptIterator it = PyOps.GetIterator(d, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                snapshot.Add(v, ctx);
            return PyOps.GetIterator(snapshot, ctx);
        }

        private static ScriptValue Rotate_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            BigInteger n = BigInteger.One;
            if (a.Length == 1)
            {
                if (a[0].Kind != ValueKind.Int && a[0].Kind != ValueKind.Bool)
                    throw Raise.TypeError(ctx, "'" + a[0].PyTypeName + "' object cannot be interpreted as an integer");
                n = Coerce.ToSsize64(ctx, NumericOps.AsBigInteger(a[0]));
            }
            else if (a.Length > 1)
            {
                throw Args.AtMostError(ctx, "rotate", 1, a.Length);
            }
            if (d._count > 1)
            {
                // Physically move elements (a _head shift alone is wrong when the ring has slack slots).
                int k = (int)(((n % d._count) + d._count) % d._count);   // normalized right-rotation in [0, count)
                if (k <= d._count - k)
                {
                    for (int t = 0; t < k; t++)
                        d.AppendLeftRaw(d.PopRaw(), ctx);         // right rotation: tail -> head
                }
                else
                {
                    for (int t = 0; t < d._count - k; t++)
                        d.AppendRaw(d.PopLeftRaw(), ctx);         // left rotation: head -> tail
                }
                d._version++;   // CPython counts a rotation of two or more items only
            }
            return ctx.Values.None;
        }

        private static ScriptValue Remove_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            ScriptValue target = Args.One(ctx, a, "remove");
            int v = d._version;
            for (int i = 0; i < d._count; i++)
            {
                bool eq = PyOps.Equals(target, d.GetLogical(i), ctx, 0);
                if (d._version != v)
                    throw Raise.IndexError(ctx, "deque mutated during iteration");   // CPython 3.14's text
                if (eq)
                {
                    d.RemoveAt(i, ctx);
                    return ctx.Values.None;
                }
            }
            throw Raise.ValueError(ctx, "deque.remove(x): x not in deque");
        }

        private static ScriptValue Count_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            ScriptValue target = Args.One(ctx, a, "count");
            long needle;
            bool fast = PyOps.TryGetSmallInt(target, out needle);
            int c = 0, v = d._version;
            for (int i = 0; i < d._count; i++)
            {
                ctx.Budget.Step();   // per-element cadence, matching ListMethods.Count
                ScriptValue el = d.GetLogical(i);
                if (fast)
                {
                    int r = PyOps.SmallIntEq(needle, el);
                    if (r >= 0)
                    {
                        c += r;
                        continue;
                    }
                }
                if (PyOps.Equals(target, el, ctx, 0))
                    c++;
                d.CheckUnchanged(v, ctx);
            }
            return ctx.Values.Int(c);
        }

        private static ScriptValue Reverse_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            for (int i = 0, j = d._count - 1; i < j; i++, j--)
            {
                int pi = d.Phys(i), pj = d.Phys(j);
                ScriptValue t = d._buf[pi];
                d._buf[pi] = d._buf[pj];
                d._buf[pj] = t;
            }
            return ctx.Values.None;   // reverse() leaves CPython's state counter alone
        }

        private static ScriptValue Clear_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            d._buf = new ScriptValue[8];
            d._head = 0;
            d._count = 0;
            d._version++;
            return ctx.Values.None;
        }

        private static ScriptValue Maxlen(ScriptValue self, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            return d._maxlen < 0 ? (ScriptValue)ctx.Values.None : ctx.Values.Int(d._maxlen);
        }

        // ---- 3.5+ API: copy / index / insert, + and * ----

        private static ScriptValue Copy_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            Args.None(ctx, a, "copy");
            return ((DequeValue)self).CloneShallow(ctx);
        }

        private static ScriptValue Index_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            Args.Unpack(ctx, a, "index", 1, 3);
            int start = a.Length >= 2 ? ClampIndex(ctx, a[1], d._count) : 0;
            int stop = a.Length == 3 ? ClampIndex(ctx, a[2], d._count) : d._count;
            int v = d._version;
            for (int i = start; i < stop; i++)
            {
                ctx.Budget.Step();
                if (PyOps.Equals(a[0], d.GetLogical(i), ctx, 0))
                    return ctx.Values.Int(i);
                d.CheckUnchanged(v, ctx);
            }
            throw Raise.ValueError(ctx, "deque.index(x): x not in deque");
        }

        private static ScriptValue Insert_(ScriptValue self, ScriptValue[] a, KwArgs kw, EvalContext ctx)
        {
            DequeValue d = (DequeValue)self;
            Args.Unpack(ctx, a, "insert", 2, 2);
            if (d._maxlen >= 0 && d._count >= d._maxlen)
                throw Raise.IndexError(ctx, "deque already at its maximum size");
            int i = ClampIndex(ctx, a[0], d._count);
            d.AppendRaw(a[1], ctx);
            for (int j = d._count - 1; j > i; j--)
            {
                int pj = d.Phys(j), pk = d.Phys(j - 1);
                ScriptValue t = d._buf[pj];
                d._buf[pj] = d._buf[pk];
                d._buf[pk] = t;
            }
            return ctx.Values.None;
        }

        // list-style slice index: negative counts from the end, then clamped into [0, count].
        private static int ClampIndex(EvalContext ctx, ScriptValue v, int count)
        {
            if (v.Kind != ValueKind.Int && v.Kind != ValueKind.Bool)
                throw Raise.TypeError(ctx, "'" + v.PyTypeName + "' object cannot be interpreted as an integer");
            BigInteger i = NumericOps.AsBigInteger(v);
            if (i < 0)
                i += count;
            if (i < 0)
                return 0;
            return i > count ? count : (int)i;
        }

        // d += iterable extends in place (any iterable, like extend); d *= n repeats in place.
        internal void ExtendFrom(ScriptValue source, EvalContext ctx)
        {
            IScriptIterator it = SourceIterator(this, source, ctx);
            ScriptValue v;
            while (it.MoveNext(ctx, out v))
                Append(v, ctx);
        }

        internal void RepeatInPlace(BigInteger n, EvalContext ctx)
        {
            if (n > long.MaxValue || n < long.MinValue)
                throw Raise.Overflow(ctx, "cannot fit 'int' into an index-sized integer");
            if (n <= 0)
            {
                Clear_(this, System.Array.Empty<ScriptValue>(), KwArgs.Empty, ctx);
                return;
            }
            int count = _count;
            if (count == 0)
                return;
            ctx.Values.PreCharge(8L * count);
            ctx.Budget.ChargeLinear(count);
            var snapshot = new ScriptValue[count];
            for (int i = 0; i < count; i++)
                snapshot[i] = GetLogical(i);
            for (BigInteger r = 1; r < n; r++)
            {
                if (_maxlen >= 0 && r * count > (BigInteger)_maxlen + count)
                    break;   // further rounds only evict what they add
                ctx.Budget.ChargeLinear(count);   // a full deque under maxlen appends without growing: no charge of its own
                for (int i = 0; i < count; i++)
                    Append(snapshot[i], ctx);
            }
        }

        // deque + deque -> a new deque with the left operand's maxlen; deque * int -> repeated copy.
        protected internal override ScriptValue BinaryOpCore(PyBinOp op, ScriptValue other, bool reflected, EvalContext ctx)
        {
            if (op == PyBinOp.Add)
            {
                if (reflected)
                    return null;
                DequeValue o = other as DequeValue;
                if (o == null)
                    throw Raise.TypeError(ctx, "can only concatenate deque (not \"" + other.PyTypeName + "\") to deque");
                DequeValue r = CloneShallow(ctx);
                r.ExtendFrom(o, ctx);
                return r;
            }
            if (op == PyBinOp.Mul && (other.Kind == ValueKind.Int || other.Kind == ValueKind.Bool))
            {
                DequeValue r = CloneShallow(ctx);
                r.RepeatInPlace(NumericOps.AsBigInteger(other), ctx);
                return r;
            }
            return null;
        }

        private static IDictionary<string, SlotDescriptor> BuildSlots()
        {
            var s = new Slots(v => ((DequeValue)v)._count);
            s.Method("append", 1, 1, Append_);
            s.Method("appendleft", 1, 1, AppendLeft_);
            s.Method("pop", 0, 0, Pop_);
            s.Method("popleft", 0, 0, PopLeft_);
            s.Method("extend", 1, 1, Extend_);
            s.Method("extendleft", 1, 1, ExtendLeft_);
            s.Linear("rotate", 0, 1, Rotate_);
            s.Linear("remove", 1, 1, Remove_);
            s.Linear("count", 1, 1, Count_);
            s.Linear("reverse", 0, 0, Reverse_);
            s.Method("clear", 0, 0, Clear_);
            s.Linear("copy", 0, 0, Copy_);
            s.Linear("index", 1, 3, Index_);
            s.Linear("insert", 2, 2, Insert_);
            s.Property("maxlen", Maxlen);
            return s.Table;
        }
    }

    // deque iterator with a mutation-during-iteration version guard.
    internal sealed class DequeIterator : ScriptIteratorBase, ICopyableIterator
    {
        public IScriptIterator CopyPosition() { return (IScriptIterator)MemberwiseClone(); }

        private readonly DequeValue _d;
        private readonly int _version;
        private int _idx;

        internal DequeIterator(DequeValue d) { _d = d; _version = d.Version; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_d.Version != _version)
                throw Raise.RuntimeError(ctx, "deque mutated during iteration");
            if (_idx >= _d.Count)
            {
                value = null;
                return false;
            }
            value = _d.GetLogical(_idx);
            _idx++;
            return true;
        }
    }

    // reversed(deque): a live cursor from the right end, as CPython's: it reads what the slot holds now, and
    // a change of the length (or a rotation) since reversed() is a RuntimeError that also ends the iterator.
    internal sealed class DequeReverseIterator : ScriptIteratorBase, ICopyableIterator
    {
        public IScriptIterator CopyPosition() { return (IScriptIterator)MemberwiseClone(); }

        private readonly DequeValue _d;
        private readonly int _version;
        private int _left;

        internal DequeReverseIterator(DequeValue d) { _d = d; _version = d.Version; _left = d.Count; }

        protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
        {
            if (_left == 0)
            {
                value = null;
                return false;
            }
            if (_d.Version != _version)
            {
                _left = 0;
                throw Raise.RuntimeError(ctx, "deque mutated during iteration");
            }
            _left--;
            value = _d.GetLogical(_left);
            return true;
        }
    }
}
