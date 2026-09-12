using System;
using System.Collections.Generic;
using Outbridge.PyLite.Runtime;
using Outbridge.PyLite.Runtime.Errors;
using Outbridge.PyLite.Runtime.Evaluator;
using Outbridge.PyLite.Runtime.Values;

namespace Outbridge.PyLite.Modules.Support
{
    // One arg/kwargs binder for every builtin and type method. Built once per function
    // in a static readonly field, sealed, then reused. Bind fills a caller-provided slots array in
    // declaration order (Req/Opt/KwOnly), with *args in a separate out. Deviation from the doc signature:
    // Bind takes ctx, because the count/keyword errors are script exceptions.
    internal sealed class ArgSpec
    {
        private enum PK { Req, Opt, KwOnly }

        private struct Param
        {
            public string Name;
            public PK Kind;
            public ScriptValue Default;   // Opt/KwOnly; may be MISSING
        }

        private readonly string _func;
        private readonly List<Param> _params = new List<Param>();
        private bool _hasStar;
        private bool _noKwargs;
        private bool _sealed;

        private int _posCount;                 // Req + Opt (positional slots)
        private int _reqCount;                 // Req
        private int[] _posSlotIdx;             // param index of the j-th positional slot

        public static readonly ScriptValue MISSING = MissingValue.Instance;

        private ArgSpec(string func) { _func = func; }

        public static ArgSpec Create(string funcName) { return new ArgSpec(funcName); }

        public ArgSpec Req(string name) { return AddParam(name, PK.Req, null); }
        public ArgSpec Opt(string name, ScriptValue def) { return AddParam(name, PK.Opt, def); }
        public ArgSpec KwOnly(string name, ScriptValue def) { return AddParam(name, PK.KwOnly, def); }

        public ArgSpec Star(string name)
        {
            RequireUnsealed();
            if (_hasStar)
                throw new InvalidOperationException("ArgSpec " + _func + ": at most one *args");
            _hasStar = true;
            return this;
        }

        public ArgSpec NoKwargs() { RequireUnsealed(); _noKwargs = true; return this; }

        private ArgSpec AddParam(string name, PK kind, ScriptValue def)
        {
            RequireUnsealed();
            _params.Add(new Param { Name = name, Kind = kind, Default = def });
            return this;
        }

        public ArgSpec Seal()
        {
            var posSlots = new List<int>();
            bool seenOpt = false;
            for (int i = 0; i < _params.Count; i++)
            {
                Param p = _params[i];
                if (p.Kind == PK.Req)
                {
                    if (seenOpt)
                        throw new InvalidOperationException("ArgSpec " + _func + ": required positional after optional");
                    _reqCount++;
                    posSlots.Add(i);
                }
                else if (p.Kind == PK.Opt)
                {
                    seenOpt = true;
                    posSlots.Add(i);
                }
            }
            _posCount = posSlots.Count;
            _posSlotIdx = posSlots.ToArray();
            _sealed = true;
            return this;
        }

        public int SlotCount { get { return _params.Count; } }

        public void Bind(EvalContext ctx, ScriptValue[] args, KwArgs kwargs, ScriptValue[] slots, out ScriptValue[] star)
        {
            for (int i = 0; i < slots.Length; i++)
                slots[i] = MISSING;

            int nPos = args == null ? 0 : args.Length;
            int placed = nPos < _posCount ? nPos : _posCount;
            for (int j = 0; j < placed; j++)
                slots[_posSlotIdx[j]] = args[j];

            if (nPos > _posCount)
            {
                if (_hasStar)
                {
                    int extra = nPos - _posCount;
                    star = new ScriptValue[extra];
                    Array.Copy(args, _posCount, star, 0, extra);
                }
                else
                {
                    throw TooMany(ctx, nPos);
                }
            }
            else
            {
                star = Array.Empty<ScriptValue>();
            }

            int nKw = kwargs.Count;
            for (int j = 0; j < nKw; j++)
            {
                if (_noKwargs)
                    throw Raise.TypeError(ctx, _func + "() takes no keyword arguments");
                string name = kwargs.NameAt(j);
                int idx = FindParam(name);
                if (idx < 0)
                    throw KwReader.InvalidError(ctx, null, name);
                if (!ReferenceEquals(slots[idx], MISSING))
                    throw Raise.TypeError(ctx, _func + "() got multiple values for argument '" + name + "'");
                slots[idx] = kwargs.ValueAt(j);
            }

            for (int i = 0; i < _params.Count; i++)
            {
                if (!ReferenceEquals(slots[i], MISSING))
                    continue;
                Param p = _params[i];
                if (p.Kind == PK.Req)
                    throw TooFew(ctx, nPos);
                slots[i] = p.Default;   // MISSING when no default was supplied
            }
        }

        private int FindParam(string name)
        {
            for (int i = 0; i < _params.Count; i++)
                if (_params[i].Name == name)
                    return i;
            return -1;
        }

        private ScriptException TooFew(EvalContext ctx, int got)
        {
            if (_reqCount == _posCount)
                return Args.ExactlyError(ctx, _func, _posCount, got);
            return Args.AtLeastError(ctx, _func, _reqCount, got);
        }

        private ScriptException TooMany(EvalContext ctx, int got)
        {
            if (_reqCount == _posCount)
                return Args.ExactlyError(ctx, _func, _posCount, got);
            return Args.AtMostError(ctx, _func, _posCount, got);
        }

        private void RequireUnsealed()
        {
            if (_sealed)
                throw new InvalidOperationException("ArgSpec " + _func + " is already sealed");
        }

        // "argument not passed" sentinel — a value distinct from None that never reaches a script.
        private sealed class MissingValue : ScriptValue
        {
            public static readonly MissingValue Instance = new MissingValue();
            private static readonly ScriptTypeInfo T = new ScriptTypeInfo("<missing>", null);
            private MissingValue() { }
            public override ScriptTypeInfo TypeInfo { get { return T; } }
            internal override ValueKind Kind { get { return ValueKind.Opaque; } }
        }
    }
}
