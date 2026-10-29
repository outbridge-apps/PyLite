using System;
using System.Collections.Generic;
using Outbridge.PyLite.Modules.Support;
using Outbridge.PyLite.Runtime.Errors;

namespace Outbridge.PyLite.Runtime.Values
{
    // What io.StringIO and io.BytesIO share: the position, the closed state, the context-manager and line
    // protocols, and the methods whose bodies do not depend on the element type. Each kind adds its buffer,
    // getvalue/write/read/readline/seek, and CPython's own wording, which differs between the two.
    internal abstract class MemoryFileValue : ScriptValue, ILineFile, IContextManager
    {
        protected long Pos;   // a BytesIO position may sit past the end: a later write pads the gap
        protected bool IsClosed;

        internal override ValueKind Kind { get { return ValueKind.Opaque; } }

        protected abstract string ClosedText { get; }
        protected abstract string NegativeSizeText { get; }
        protected abstract int ContentLength { get; }

        // truncate(): the content is cut to `length`, never extended; the position stays.
        protected abstract void Shrink(int length);

        // Writes one item of the file's element type, refusing any other; the count written.
        protected abstract long WriteItem(EvalContext ctx, ScriptValue item);

        // The next line (whole when size < 0, else at most size elements); null at the end.
        protected abstract ScriptValue ReadLine(EvalContext ctx, long size, out int length);

        internal virtual void Close() { IsClosed = true; }   // idempotent

        internal void CheckOpen(EvalContext ctx)
        {
            if (IsClosed)
                throw Raise.ValueError(ctx, ClosedText);
        }

        protected internal override IScriptIterator GetIteratorCore(EvalContext ctx) { return new LineIterator(this); }

        // A context manager as in CPython: entering checks the file is open, leaving closes it.
        public ScriptValue Enter(EvalContext ctx)
        {
            CheckOpen(ctx);
            return this;
        }

        public bool Exit(EvalContext ctx, ExceptionValue pending)
        {
            Close();
            return false;
        }

        public bool NextLine(EvalContext ctx, out ScriptValue line)
        {
            CheckOpen(ctx);
            int unused;
            line = ReadLine(ctx, -1, out unused);
            return line != null;
        }

        protected static T Open<T>(ScriptValue self, EvalContext ctx) where T : MemoryFileValue
        {
            var f = (T)self;
            f.CheckOpen(ctx);
            return f;
        }

        protected static Dictionary<string, SlotDescriptor> CommonSlots()
        {
            var s = new Dictionary<string, SlotDescriptor>(StringComparer.Ordinal);
            s["writelines"] = SlotDescriptor.MakeMethod("writelines", (self, a, kw, ctx) =>
            {
                Args.Exactly(ctx, a, kw, "writelines", 1);
                MemoryFileValue f = Open<MemoryFileValue>(self, ctx);
                IScriptIterator it = PyOps.GetIterator(a[0], ctx);
                ScriptValue item;
                while (it.MoveNext(ctx, out item))
                    f.WriteItem(ctx, item);
                return ctx.Values.None;
            });
            s["readlines"] = SlotDescriptor.MakeMethod("readlines", Args.Positional("readlines", 0, 1, (self, a, kw, ctx) =>
            {
                MemoryFileValue f = Open<MemoryFileValue>(self, ctx);
                long hint = FileArgs.OptionalSize(ctx, a, 0);
                ListValue list = ctx.Values.List(4);
                long total = 0;
                ScriptValue line;
                int length;
                while ((line = f.ReadLine(ctx, -1, out length)) != null)
                {
                    ctx.Budget.Step();
                    list.Add(line, ctx);
                    total += length;
                    if (hint > 0 && total >= hint)
                        break;
                }
                return list;
            }));
            s["tell"] = SlotDescriptor.MakeMethod("tell", (self, a, kw, ctx) =>
            {
                Args.None(ctx, a, kw, "tell");
                return ctx.Values.Int(Open<MemoryFileValue>(self, ctx).Pos);
            });
            s["truncate"] = SlotDescriptor.MakeMethod("truncate", (self, a, kw, ctx) =>
            {
                Args.AtMost(ctx, a, kw, "truncate", 1);
                MemoryFileValue f = Open<MemoryFileValue>(self, ctx);
                long size = f.Pos;
                if (a.Length == 1 && a[0].Kind != ValueKind.None)
                {
                    size = FileArgs.OptionalSize(ctx, a, 0);
                    if (size < 0)
                        throw Raise.ValueError(ctx, f.NegativeSizeText + " " + size);
                }
                if (size < f.ContentLength)
                    f.Shrink((int)size);
                return ctx.Values.Int(size);
            });
            s["close"] = SlotDescriptor.MakeMethod("close", Args.Positional("close", 0, 0, (self, a, kw, ctx) =>
            {
                ((MemoryFileValue)self).Close();
                return ctx.Values.None;
            }));
            s["flush"] = SlotDescriptor.MakeMethod("flush", Answer("flush", null));
            s["readable"] = SlotDescriptor.MakeMethod("readable", Answer("readable", true));
            s["writable"] = SlotDescriptor.MakeMethod("writable", Answer("writable", true));
            s["seekable"] = SlotDescriptor.MakeMethod("seekable", Answer("seekable", true));
            s["isatty"] = SlotDescriptor.MakeMethod("isatty", Answer("isatty", false));
            s["closed"] = SlotDescriptor.MakeProperty("closed", (self, ctx) => ctx.Values.Bool(((MemoryFileValue)self).IsClosed));
            return s;
        }

        // A no-argument query of an open file: a fixed bool, or None.
        protected static BuiltinDelegate Answer(string name, bool? answer)
        {
            return Args.Positional(name, 0, 0, (self, a, kw, ctx) =>
            {
                Open<MemoryFileValue>(self, ctx);
                return answer.HasValue ? ctx.Values.Bool(answer.Value) : (ScriptValue)ctx.Values.None;
            });
        }

        private sealed class LineIterator : ScriptIteratorBase
        {
            private readonly MemoryFileValue _file;
            internal LineIterator(MemoryFileValue file) { _file = file; }

            protected override bool MoveNextCore(EvalContext ctx, out ScriptValue value)
            {
                return _file.NextLine(ctx, out value);
            }
        }
    }
}
